using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GraphQL.Execution;
using Nekoyume.TableData;
using NineChronicles.Headless.GraphTypes.States.Models.Table;
using Xunit;
using static NineChronicles.Headless.Tests.GraphQLTestUtils;

namespace NineChronicles.Headless.Tests.GraphTypes.States.Models
{
    public class StakeRegularRewardRowTypeTest
    {
        [Fact]
        public async Task Query()
        {
            const string query = @"
            {
                level
                requiredGold
                rewards {
                    itemId
                    rate
                }
                bonusRewards {
                    itemId
                    count
                }
            }";

            const int level = 1;
            StakeRegularRewardSheet.Row rewardRow = Fixtures.TableSheetsFX.StakeRegularRewardSheet[level]!;
            StakeRegularFixedRewardSheet.Row fixedRewardRow = Fixtures.TableSheetsFX.StakeRegularFixedRewardSheet[level]!;
            StakeRegularRewardSheet.RewardInfo[] rewards = rewardRow.Rewards.ToArray();
            StakeRegularFixedRewardSheet.RewardInfo[] bonusRewards = fixedRewardRow.Rewards.ToArray();
            Assert.Equal(2, rewards.Length);
            Assert.Single(bonusRewards);
            var queryResult = await ExecuteQueryAsync<StakeRegularRewardRowType>(
                query,
                source: (rewardRow.Level, rewardRow.RequiredGold, rewards, bonusRewards) 
            );
            var data = (Dictionary<string, object>)((ExecutionNode) queryResult.Data!).ToValue()!;
            var expected = new Dictionary<string, object>
            {
                ["level"] = rewardRow.Level,
                ["requiredGold"] = rewardRow.RequiredGold,
                ["rewards"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["itemId"] = rewards[0].ItemId,
                        ["rate"] = rewards[0].Rate,
                    },
                    new Dictionary<string, object>
                    {
                        ["itemId"] = rewards[1].ItemId,
                        ["rate"] = rewards[1].Rate,
                    },
                },
                ["bonusRewards"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["itemId"] = bonusRewards[0].ItemId,
                        ["count"] = bonusRewards[0].Count,
                    },
                }
            };
            Assert.Equal(data, expected);
            // Assert.Equal(row.Level, data["level"]);
            // Assert.Equal(row.RequiredGold, data["requiredGold"]);
            // var dataRewards = Assert.IsType<Dictionary<string, object>[]>(data["rewards"]).ToList();
            // Assert.Single(dataRewards);
            // Assert.Equal(rewards.First().ItemId, dataRewards[0]["itemId"]);
            // Assert.Equal(rewards.First().Rate, dataRewards[0]["rate"]);
        }
    }
}
