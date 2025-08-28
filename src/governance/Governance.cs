using EpicChain.SmartContract.Framework;
using EpicChain.SmartContract.Framework.Native;
using EpicChain.SmartContract.Framework.Services;
using System.ComponentModel;
using System.Numerics;
using EpicChain.Contracts.Utils;
using EpicChain.Contracts.Interfaces;

namespace EpicChain.Contracts.Governance
{
    [DisplayName("EpicGovernance")]
    public class Governance : SmartContract
    {
        // Roles
        public static readonly byte[] ADMIN_ROLE = "ADMIN_ROLE";
        public static readonly byte[] PROPOSER_ROLE = "PROPOSER_ROLE";
        public static readonly byte[] DEFAULT_ADMIN_ROLE = "DEFAULT_ADMIN_ROLE";

        // Events
        public delegate void OnProposalCreated(BigInteger proposalId, UInt160 proposer, UInt160[] targets, BigInteger[] values, string[] calldatas, BigInteger startTime, BigInteger endTime, string description);
        public static event OnProposalCreated onProposalCreated;

        public delegate void OnVoteCast(BigInteger proposalId, UInt160 voter, int support, BigInteger votes);
        public static event OnVoteCast onVoteCast;

        public delegate void OnProposalQueued(BigInteger proposalId, BigInteger eta);
        public static event OnProposalQueued onProposalQueued;

        public delegate void OnProposalExecuted(BigInteger proposalId);
        public static event OnProposalExecuted onProposalExecuted;

        public delegate void OnProposalCanceled(BigInteger proposalId);
        public static event OnProposalCanceled onProposalCanceled;

        // Storage
        private static StorageMap Proposals => new StorageMap(Storage.CurrentContext, "proposals");
        private static StorageMap Votes => new StorageMap(Storage.CurrentContext, "votes");
        private static StorageMap ProposalCount => new StorageMap(Storage.CurrentContext, "proposal_count");
        private static StorageMap Settings => new StorageMap(Storage.CurrentContext, "settings");

        private const string VotingPeriodKey = "vp";
        private const string QuorumKey = "q";

        // Governance Token
        private static readonly UInt160 GovernanceTokenAddress = (UInt160)new byte[] { /* Governance Token Address */ };

        // TimeLock Contract
        private static readonly UInt160 TimeLockAddress = (UInt160)new byte[] { /* TimeLock Contract Address */ };

        [DisplayName("_deploy")]
        public static void Deploy(object data, bool update)
        {
            if (update) return;
            var tx = (Transaction)Runtime.ScriptContainer;
            Roles.GrantRole(DEFAULT_ADMIN_ROLE, tx.Sender);
            Settings.Put(VotingPeriodKey, 17280); // 1 day in blocks (assuming 15s block time)
            Settings.Put(QuorumKey, 4); // 4% quorum
        }

        public static void SetVotingPeriod(BigInteger period)
        {
            Roles.RequireRole(ADMIN_ROLE, (UInt160)Runtime.CallingScriptHash);
            Settings.Put(VotingPeriodKey, period);
        }

        public static void SetQuorum(BigInteger quorum)
        {
            Roles.RequireRole(ADMIN_ROLE, (UInt160)Runtime.CallingScriptHash);
            Settings.Put(QuorumKey, quorum);
        }

        public static BigInteger Propose(UInt160[] targets, BigInteger[] values, string[] calldatas, string description)
        {
            Roles.RequireRole(PROPOSER_ROLE, (UInt160)Runtime.CallingScriptHash);

            BigInteger proposalId = (BigInteger)ProposalCount.Get("count") + 1;
            ProposalCount.Put("count", proposalId);

            BigInteger startTime = Runtime.Time;
            BigInteger votingPeriod = (BigInteger)Settings.Get(VotingPeriodKey);
            BigInteger endTime = startTime + votingPeriod;

            var proposal = new Proposal
            {
                proposer = (UInt160)Runtime.CallingScriptHash,
                targets = targets,
                values = values,
                calldatas = calldatas,
                startTime = startTime,
                endTime = endTime,
                description = description,
                forVotes = 0,
                againstVotes = 0,
                abstainVotes = 0,
                canceled = false,
                executed = false
            };

            Proposals.Put(proposalId.ToByteArray(), StdLib.Serialize(proposal));

            onProposalCreated(proposalId, proposal.proposer, targets, values, calldatas, startTime, endTime, description);

            return proposalId;
        }

        public static void Vote(BigInteger proposalId, int support)
        {
            var proposal = GetProposal(proposalId);
            if (proposal.startTime > Runtime.Time || proposal.endTime < Runtime.Time) throw new Exception("Voting is not active");

            var voter = (UInt160)Runtime.CallingScriptHash;
            var votes = (BigInteger)Contract.Call(GovernanceTokenAddress, "getVotes", CallFlags.ReadOnly, voter);

            var receipt = GetVote(proposalId, voter);
            if (receipt.hasVoted) throw new Exception("Already voted");

            if (support == 0) // Against
            {
                proposal.againstVotes += votes;
            }
            else if (support == 1) // For
            {
                proposal.forVotes += votes;
            }
            else if (support == 2) // Abstain
            {
                proposal.abstainVotes += votes;
            }
            else
            {
                throw new Exception("Invalid vote type");
            }

            Proposals.Put(proposalId.ToByteArray(), StdLib.Serialize(proposal));

            var newReceipt = new VoteReceipt { hasVoted = true, support = support, votes = votes };
            Votes.Put(proposalId.ToByteArray().Concat(voter), StdLib.Serialize(newReceipt));

            onVoteCast(proposalId, voter, support, votes);
        }

        public static void Queue(BigInteger proposalId)
        {
            var proposal = GetProposal(proposalId);
            if (proposal.endTime > Runtime.Time) throw new Exception("Voting is still active");
            if (proposal.forVotes <= proposal.againstVotes) throw new Exception("Proposal was not successful");

            BigInteger quorum = (BigInteger)Settings.Get(QuorumKey);
            BigInteger totalSupply = (BigInteger)Contract.Call(GovernanceTokenAddress, "totalSupply", CallFlags.ReadOnly);
            if (proposal.forVotes * 100 / totalSupply < quorum) throw new Exception("Quorum not reached");

            BigInteger eta = Runtime.Time + (BigInteger)Contract.Call(TimeLockAddress, "getDelay", CallFlags.ReadOnly);
            proposal.eta = eta;
            Proposals.Put(proposalId.ToByteArray(), StdLib.Serialize(proposal));

            Contract.Call(TimeLockAddress, "queueTransaction", CallFlags.All, proposal.targets, proposal.values, proposal.calldatas, proposal.description, eta);

            onProposalQueued(proposalId, eta);
        }

        public static void Execute(BigInteger proposalId)
        {
            var proposal = GetProposal(proposalId);
            if (proposal.eta > Runtime.Time) throw new Exception("Timelock has not expired");
            if (proposal.executed) throw new Exception("Proposal already executed");

            proposal.executed = true;
            Proposals.Put(proposalId.ToByteArray(), StdLib.Serialize(proposal));

            Contract.Call(TimeLockAddress, "executeTransaction", CallFlags.All, proposal.targets, proposal.values, proposal.calldatas, proposal.description);

            onProposalExecuted(proposalId);
        }

        public static void Cancel(BigInteger proposalId)
        {
            var proposal = GetProposal(proposalId);
            if (proposal.proposer != (UInt160)Runtime.CallingScriptHash) throw new Exception("Only proposer can cancel");
            if (proposal.executed) throw new Exception("Proposal already executed");

            proposal.canceled = true;
            Proposals.Put(proposalId.ToByteArray(), StdLib.Serialize(proposal));

            onProposalCanceled(proposalId);
        }

        [Safe]
        public static Proposal GetProposal(BigInteger proposalId)
        {
            var data = Proposals.Get(proposalId.ToByteArray());
            if (data == null) throw new Exception("Proposal not found");
            return (Proposal)StdLib.Deserialize(data);
        }

        [Safe]
        public static VoteReceipt GetVote(BigInteger proposalId, UInt160 voter)
        {
            var data = Votes.Get(proposalId.ToByteArray().Concat(voter));
            if (data == null) return new VoteReceipt { hasVoted = false };
            return (VoteReceipt)StdLib.Deserialize(data);
        }

        public struct Proposal
        {
            public UInt160 proposer;
            public UInt160[] targets;
            public BigInteger[] values;
            public string[] calldatas;
            public BigInteger startTime;
            public BigInteger endTime;
            public string description;
            public BigInteger forVotes;
            public BigInteger againstVotes;
            public BigInteger abstainVotes;
            public bool canceled;
            public bool executed;
            public BigInteger eta;
        }

        public struct VoteReceipt
        {
            public bool hasVoted;
            public int support;
            public BigInteger votes;
        }
    }
}