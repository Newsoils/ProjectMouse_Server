using System.Collections;
using System.Collections.Generic;
 
namespace CLIP
{
    namespace  Core_Tools
    {
      
            public class General_Function
            {
                public static string parse_minutes_int_to_str(int minute_number)
                {
                    return minute_number / 60 + " h " + minute_number % 60 + " min"; 
                }
                public static List<int> weight_sample(in List<float> weight,int count)
                {
                    var ans=new List<int>();
                    for (int i = 0; i < count; i++) {

                        ans.Add(weight_sample(weight));
                    }
                        return ans;
                }

                public static int weight_sample(in List<float> weight)
                {
                    var rd = new System.Random((int)System.DateTime.Now.Millisecond);
                    double total = 0;
                    foreach (var x in weight)
                    {
                        if (x <= 0) continue;
                        total += (double)x;
                    }
                    double random_val = rd.NextDouble()*total;
                    int ans = 0;
                    double up = 0;
                    for(int i=0;i< weight.Count; i++)
                    {

                        if (weight[i] <= 0) continue;
                        up += weight[i];
                        if(up> random_val)
                        {
                            return i;
                        }
                    }
                    return ans;
                }
             
            }

    }
}