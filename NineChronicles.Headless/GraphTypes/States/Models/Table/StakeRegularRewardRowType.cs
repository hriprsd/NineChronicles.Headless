using GraphQL.Types;
using Nekoyume.TableData;

namespace NineChronicles.Headless.GraphTypes.States.Models.Table
{
    public class StakeRegularRewardRowType : ObjectGraphType<(int Level, long RequiredGold, StakeRegularRewardSheet.RewardInfo[] Rewards, StakeRegularFixedRewardSheet.RewardInfo[] BonusRewards)>
    {
        public StakeRegularRewardRowType()
        {
            Field<NonNullGraphType<IntGraphType>>(
                nameof(StakeRegularRewardSheet.Row.Level),
                resolve: context => context.Source.Level
            );
            Field<NonNullGraphType<LongGraphType>>(
                nameof(StakeRegularRewardSheet.Row.RequiredGold),
                resolve: context => context.Source.RequiredGold
            );
            Field<NonNullGraphType<ListGraphType<NonNullGraphType<StakeRegularRewardInfoType>>>>(
                nameof(StakeRegularRewardSheet.Row.Rewards),
                resolve: context => context.Source.Rewards
            );
            Field<NonNullGraphType<ListGraphType<NonNullGraphType<StakeRegularFixedRewardInfoType>>>>(
                "bonusRewards",
                resolve: context => context.Source.BonusRewards
            );
        }
    }
}
