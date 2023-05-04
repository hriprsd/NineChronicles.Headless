using GraphQL;
using Libplanet;
using Libplanet.Action;
using Libplanet.Action.Sys;
using Libplanet.Assets;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Blocks;
using Libplanet.Consensus;
using Libplanet.Crypto;
using Libplanet.Headless.Hosting;
using Libplanet.KeyStore;
using Libplanet.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nekoyume.Action;
using Nekoyume.Model.State;
using Nekoyume.TableData;
using Nekoyume.BlockChain.Policy;
using NineChronicles.Headless.GraphTypes;
using NineChronicles.Headless.Tests.Common;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Bencodex.Types;
using Libplanet.Tx;
using Xunit.Abstractions;
using NCAction = Libplanet.Action.PolymorphicAction<Nekoyume.Action.ActionBase>;

namespace NineChronicles.Headless.Tests.GraphTypes
{
    public class GraphQLTestBase
    {
        protected ITestOutputHelper _output;

        public GraphQLTestBase(ITestOutputHelper output)
        {
            Log.Logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Console().CreateLogger();

            _output = output;

#pragma warning disable CS0618
            // Use of obsolete method Currency.Legacy(): https://github.com/planetarium/lib9c/discussions/1319
            var goldCurrency = Currency.Legacy("NCG", 2, null);
#pragma warning restore CS0618

            var sheets = TableSheetsImporter.ImportSheets();
            var blockAction = new RewardGold();
            var genesisBlock = BlockChain<NCAction>.ProposeGenesisBlock(
                transactions: ImmutableList<Transaction>.Empty.Add(Transaction.Create<NCAction>(0,
                    AdminPrivateKey, null, new NCAction[]
                    {
                        new InitializeStates(
                            rankingState: new RankingState0(),
                            shopState: new ShopState(),
                            gameConfigState: new GameConfigState(sheets[nameof(GameConfigSheet)]),
                            redeemCodeState: new RedeemCodeState(
                                Bencodex.Types.Dictionary.Empty
                                    .Add("address", RedeemCodeState.Address.Serialize())
                                    .Add("map", Bencodex.Types.Dictionary.Empty)
                            ),
                            adminAddressState: new AdminState(AdminAddress, 10000),
                            activatedAccountsState: new ActivatedAccountsState(),
                            goldCurrencyState: new GoldCurrencyState(goldCurrency),
                            goldDistributions: new GoldDistribution[] { },
                            tableSheets: sheets,
                            pendingActivationStates: new PendingActivationState[] { }
                        ),
                    })).AddRange(new IAction[]
                {
                    new Initialize(
                        new ValidatorSet(
                            new[] { new Validator(ProposerPrivateKey.PublicKey, BigInteger.One) }
                                .ToList()),
                        states: ImmutableDictionary.Create<Address, IValue>())
                }.Select((sa, nonce) => Transaction.Create<NCAction>(nonce + 1, AdminPrivateKey, null, new[] { sa }))),
                blockAction: blockAction,
                privateKey: AdminPrivateKey);

            var ncService = ServiceBuilder.CreateNineChroniclesNodeService(genesisBlock, ProposerPrivateKey);
            var tempKeyStorePath = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
            var keyStore = new Web3KeyStore(tempKeyStorePath);

            StandaloneContextFx = new StandaloneContext
            {
                KeyStore = keyStore,
                DifferentAppProtocolVersionEncounterInterval = TimeSpan.FromSeconds(1),
                NotificationInterval = TimeSpan.FromSeconds(1),
                NodeExceptionInterval = TimeSpan.FromSeconds(1),
                MonsterCollectionStateInterval = TimeSpan.FromSeconds(1),
                MonsterCollectionStatusInterval = TimeSpan.FromSeconds(1),
            };
            ncService.ConfigureContext(StandaloneContextFx);

            var configurationBuilder = new ConfigurationBuilder();
            var configuration = configurationBuilder.Build();

            var services = new ServiceCollection();
            var publisher = new ActionEvaluationPublisher(
                ncService.BlockRenderer,
                ncService.ActionRenderer,
                ncService.ExceptionRenderer,
                ncService.NodeStatusRenderer,
                "",
                0,
                new RpcContext(),
                new ConcurrentDictionary<string, Sentry.ITransaction>()
            );
            services.AddSingleton(publisher);
            services.AddSingleton(StandaloneContextFx);
            services.AddSingleton<IConfiguration>(configuration);
            services.AddGraphTypes();
            services.AddLibplanetExplorer<NCAction>();
            services.AddSingleton(ncService);
            services.AddSingleton(ncService.Store);
            ServiceProvider serviceProvider = services.BuildServiceProvider();
            Schema = new StandaloneSchema(serviceProvider);

            DocumentExecutor = new DocumentExecuter();
        }

        protected PrivateKey AdminPrivateKey { get; } = new PrivateKey();

        protected Address AdminAddress => AdminPrivateKey.ToAddress();

        protected PrivateKey ProposerPrivateKey { get; } = new PrivateKey();

        protected List<PrivateKey> GenesisValidators
        {
            get => new List<PrivateKey>
            {
                ProposerPrivateKey
            }.OrderBy(key => key.ToAddress()).ToList();
        }

        protected StandaloneSchema Schema { get; }

        protected StandaloneContext StandaloneContextFx { get; }

        protected BlockChain<NCAction> BlockChain =>
            StandaloneContextFx.BlockChain!;

        protected IKeyStore KeyStore =>
            StandaloneContextFx.KeyStore!;

        protected IDocumentExecuter DocumentExecutor { get; }

        protected SubscriptionDocumentExecuter SubscriptionDocumentExecuter { get; } = new SubscriptionDocumentExecuter();

        protected Task<ExecutionResult> ExecuteQueryAsync(string query)
        {
            return DocumentExecutor.ExecuteAsync(new ExecutionOptions
            {
                Query = query,
                Schema = Schema,
            });
        }
        protected Task<ExecutionResult> ExecuteSubscriptionQueryAsync(string query)
        {
            return SubscriptionDocumentExecuter.ExecuteAsync(new ExecutionOptions
            {
                Query = query,
                Schema = Schema,
            });
        }
        protected async Task<Task> StartAsync<T>(
            Swarm<T> swarm,
            CancellationToken cancellationToken = default
        )
            where T : IAction, new()
        {
            Task task = swarm.StartAsync(
                dialTimeout: TimeSpan.FromMilliseconds(200),
                broadcastBlockInterval: TimeSpan.FromMilliseconds(200),
                broadcastTxInterval: TimeSpan.FromMilliseconds(200),
                cancellationToken: cancellationToken
            );
            await swarm.WaitForRunningAsync();
            return task;
        }

        protected LibplanetNodeService<T> CreateLibplanetNodeService<T>(
            Block genesisBlock,
            AppProtocolVersion appProtocolVersion,
            PublicKey appProtocolVersionSigner,
            Progress<PreloadState>? preloadProgress = null,
            IEnumerable<BoundPeer>? peers = null,
            ImmutableList<BoundPeer>? consensusSeeds = null,
            ImmutableList<BoundPeer>? consensusPeers = null)
            where T : IAction, new()
        {
            var consensusPrivateKey = new PrivateKey();

            var properties = new LibplanetNodeServiceProperties
            {
                Host = System.Net.IPAddress.Loopback.ToString(),
                AppProtocolVersion = appProtocolVersion,
                GenesisBlock = genesisBlock,
                StoreStatesCacheSize = 2,
                StorePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
                SwarmPrivateKey = new PrivateKey(),
                ConsensusPrivateKey = consensusPrivateKey,
                ConsensusPort = null,
                Port = null,
                NoMiner = true,
                Render = false,
                Peers = peers ?? ImmutableHashSet<BoundPeer>.Empty,
                TrustedAppProtocolVersionSigners = ImmutableHashSet<PublicKey>.Empty.Add(appProtocolVersionSigner),
                IceServers = ImmutableList<IceServer>.Empty,
                ConsensusSeeds = consensusSeeds ?? ImmutableList<BoundPeer>.Empty,
                ConsensusPeers = consensusPeers ?? ImmutableList<BoundPeer>.Empty,
            };

            return new LibplanetNodeService<T>(
                properties,
                blockPolicy: new BlockPolicy<T>(),
                stagePolicy: new VolatileStagePolicy<T>(),
                renderers: new[] { new DummyRenderer<T>() },
                preloadProgress: preloadProgress,
                exceptionHandlerAction: (code, msg) => throw new Exception($"{code}, {msg}"),
                preloadStatusHandlerAction: isPreloadStart => { },
                actionLoader: StaticActionLoaderSingleton.Instance
            );
        }

        protected BlockCommit? GenerateBlockCommit(long height, BlockHash hash, List<PrivateKey> validators)
        {
            return height != 0
                ? new BlockCommit(
                    height,
                    0,
                    hash,
                    validators.Select(validator => new VoteMetadata(
                            height,
                            0,
                            hash,
                            DateTimeOffset.UtcNow,
                            validator.PublicKey,
                            VoteFlag.PreCommit).Sign(validator)).ToImmutableArray())
                : (BlockCommit?)null;
        }
    }
}
