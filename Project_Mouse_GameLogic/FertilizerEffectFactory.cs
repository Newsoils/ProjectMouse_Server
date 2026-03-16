using Project_Mouse_DataLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Project_Mouse_GameLogic
{
    public static class FertilizerEffectFactory
    {
        private static readonly Dictionary<string, Func<FertilizerEffect>> _effectFactories = new Dictionary<string, Func<FertilizerEffect>>
        {
            { "Grow", () => new GrowStageJumpEffect() },
            { "ChangeVariantChance", () => new ChangeVarientChanceEffect() }
        };


        public static FertilizerEffect GetEffect(int fertilizerId)
        {
            var data = DataManager.Instance.fertilizerData[fertilizerId];
            if(_effectFactories.TryGetValue(data.effectKey, out var factory))
            {
                var effect = factory();
                effect.effectParams = data.effectParams;
                return effect;
            }
            return null;
        }
    }
    public abstract class FertilizerEffect
    {
        public List<float> effectParams = new List<float>();
        public abstract void Apply(PlantLocalData plant);
    }
    public class GrowStageJumpEffect : FertilizerEffect
    {
        public override void Apply(PlantLocalData plant)
        {
            plant.growStage = 4;
            plant.curGrowTime = 0;
            PlantManager.UpdatePlantGrowth(plant);
            PlantManager.CheckVariant(plant);
        }
    }
    public class ChangeVarientChanceEffect : FertilizerEffect
    {
        public override void Apply(PlantLocalData plant)
        {
            plant.variantProbability = effectParams[0] / 100;
        }
    }
}
