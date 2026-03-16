using DataStructures.RandomSelector;
using Project_Mouse_DataLoader;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Project_Mouse_GameLogic
{
    public class PlantManager
    {
        public static void UpdatePlantGrowth(PlantLocalData plant)
        {
            if (plant == null)
                return;
            if(!DataManager.Instance.plantDatas.TryGetValue(plant.plantId, out var data))
            {
                Console.WriteLine("不存在的植物ID");
                return;
            }
            List<float> stageDurations = data.lifeCycle;
            double currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // 如果植物已完成所有阶段（例如已枯萎或收获），则不再更新
            if (plant.growStage >= stageDurations.Count)
            {
                plant.lastUpdateTime = currentTime;
                return;
            }

            // 计算时间差（秒），并转换为分钟
            double deltaSeconds = currentTime - plant.lastUpdateTime;
            if (deltaSeconds <= 0)
            {
                // 时间未流逝或时钟回拨，仅更新时间戳
                plant.lastUpdateTime = currentTime;
                return;
            }
            double remainingMinutes = deltaSeconds / 60.0;
            double waterRemaining = plant.water / 0.069f;
            double waterTime = Math.Min(remainingMinutes, waterRemaining); // 水分充足的分钟数
            double dryTime = remainingMinutes - waterTime;          // 缺水分钟数

            // 有效生长时间（等效正常速度）
            double effectiveGrowth = waterTime + 0.7 * dryTime;
            plant.curGrowTime += effectiveGrowth;

            // 更新水分（注意不能低于0）
            plant.water -= (float)(0.069f * remainingMinutes);
            if (plant.water < 0) plant.water = 0;
            // 循环处理，直到时间耗尽或植物达到最终阶段
            while (plant.curGrowTime > data.lifeCycle[plant.growStage] && data.lifeCycle[plant.growStage] >= 0)
            {
                plant.curGrowTime -= data.lifeCycle[plant.growStage];
                plant.growStage++;
                if(plant.growStage == 4)
                {
                    CheckVariant(plant);
                }
            }

            // 更新最后计算时间戳
            plant.lastUpdateTime = currentTime;
        }
        public static void WaterPlant(PlantLocalData plant)
        {
            if (plant == null) return;
            UpdatePlantGrowth(plant);
            plant.water = 100;
        }
        public static PlantLocalData PlantPlant(int plantId, string potUid)
        {
            if (!DataManager.Instance.plantDatas.TryGetValue(plantId, out var data))
            {
                Console.WriteLine("不存在的植物ID");
                return null;
            }
            if(potUid == null)
            {
                Console.WriteLine("花盆为null");
                return null;
            }
            PlantLocalData plant = new PlantLocalData();
            plant.plantId = plantId;
            plant.potUid = potUid;
            plant.uid = Guid.NewGuid().ToString();
            plant.water = 0;
            plant.curGrowTime = 0;
            plant.plantTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            plant.lastUpdateTime = plant.plantTime;
            plant.variantProbability = 0.03f;
            plant.harvestTime = data.harvestTime;
            plant.variantKey = "default";
            return plant;
        }
        public static void HarvestPlant(PlantLocalData plant)
        {
            UpdatePlantGrowth(plant);
            if (!DataManager.Instance.plantDatas.TryGetValue(plant.plantId, out var data))
            {
                Console.WriteLine("不存在的植物ID");
                return;
            }
            if(plant.growStage != 4)//检查是否是成熟期
            {
                Console.WriteLine("该植物未成熟或已枯萎，检查客户端逻辑");
                return;
            }
            if(data.harvestType == PlantHarvestType.CanCycle)
            {
                plant.harvestTime--;
                plant.variantProbability = Math.Max(0.5f * plant.variantProbability, 0.03f);
                plant.isFertilize = false;
                if (plant.harvestTime <= 0)
                {
                    plant.growStage++;
                }
                else
                {
                    plant.growStage = 2;
                    plant.curGrowTime = 0;
                }
            }
            else
            {
                //当收获次数为-1时移除植物
                plant.harvestTime = -1;
            }

        }
        public static bool FertilizePlant(PlantLocalData plant, int fertilizerId)
        {
            UpdatePlantGrowth(plant);
            if(plant.growStage >= 4 || plant.isFertilize)
            {
                Console.WriteLine("当前植物不可施肥，检查客户端逻辑");
                return false;
            }
            var effect = FertilizerEffectFactory.GetEffect(fertilizerId);
            if(effect == null)
            {
                Console.WriteLine("不存在的肥料效果，检查肥料数据");
                return false;
            }
            effect.Apply(plant);
            plant.isFertilize = true;
            return true;
        }
        public static void CheckVariant(PlantLocalData plant)
        {
            if (!DataManager.Instance.plantDatas.TryGetValue(plant.plantId, out var data))
            {
                Console.WriteLine("不存在的植物ID");
                return;
            }
            if(plant.variantKey != "default" && !string.IsNullOrEmpty(plant.variantKey))
            {
                return;
            }
            DynamicRandomSelector<bool> isVariant = new DynamicRandomSelector<bool>();
            isVariant.Add(true, plant.variantProbability);
            isVariant.Add(false, 1 - plant.variantProbability);
            isVariant.Build();
            if(isVariant.SelectRandomItem())
            {
                if(data.variantKey.Count > 1)
                {
                    plant.variantKey = data.variantKey[1];
                }
            }
        }
    }
}
