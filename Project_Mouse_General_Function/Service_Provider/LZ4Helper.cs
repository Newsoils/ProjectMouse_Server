using System;
using K4os.Compression.LZ4;
using System.Text;


#if UNITY_STANDALONE || UNITY_IOS ||UNITY_ANDROID||UNITY_WEBGL
using UnityEngine;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif
#if !ON_UNITY
using GF_LP = CLIP.Core_Tools.Logging_Provider;
#endif

namespace CLIP.Core_Tools
{
    /// <summary>
    /// LZ4 压缩工具类
    /// 用于将字符串快速压缩为 byte[]，或解压缩回字符串。
    /// 可用于网络传输或本地数据持久化。
    /// </summary>
    public static class LZ4_Helper
    {
        /// <summary>
        /// 使用 LZ4 高压缩比模式压缩字符串。
        /// </summary>
        /// <param name="input">原始字符串</param>
        /// <returns>压缩后的字节数组（前4字节记录原始长度）</returns>
        public static byte[] Encode(string input)
        {
            byte[] source = Encoding.UTF8.GetBytes(input);
            int maxSize = LZ4Codec.MaximumOutputSize(source.Length);
            byte[] target = new byte[maxSize + 4];

            // 预留前4字节用于存储原始长度
            int encodedLength = LZ4Codec.Encode(
                source, 0, source.Length,
                target, 4, maxSize, LZ4Level.L09_HC);

            // 写入原始数据长度（大端序）
            byte[] lenBytes = BitConverter.GetBytes(source.Length);
            Array.Reverse(lenBytes);
            Array.Copy(lenBytes, 0, target, 0, 4);

            // 截取有效部分
            byte[] result = new byte[encodedLength + 4];
            Array.Copy(target, 0, result, 0, encodedLength + 4);

            // 打印压缩比日志
            float ratio = (float)result.Length / source.Length;
            //string log = $"[LZ4] Compression OK - Ratio={ratio:F2}, Saved={source.Length - result.Length} bytes";

//#if UNITY_ENGINE
//            Debug.Log(log);
//#elif !ON_UNITY
//            GF_LP.log(log);
//#endif
            return result;
        }

        /// <summary>
        /// 解压缩 LZ4 压缩数据。
        /// </summary>
        /// <param name="input">压缩后的数据（前4字节记录原始长度）</param>
        /// <returns>还原的字符串</returns>
        public static string Decode(byte[] input)
        {
            byte[] lenBytes = { input[0], input[1], input[2], input[3] };
            Array.Reverse(lenBytes);
            int originalLength = BitConverter.ToInt32(lenBytes, 0);

            byte[] target = new byte[originalLength];
            int decoded = LZ4Codec.Decode(input, 4, input.Length - 4, target, 0, target.Length);
            return Encoding.UTF8.GetString(target, 0, decoded);
        }



        /// <summary>
        /// 输出当前运行平台（Unity 环境下）。
        /// </summary>
        public static void LogUnityPlatform()
        {
#if UNITY_STANDALONE
            Debug.Log("Running on UNITY_STANDALONE");
#elif UNITY_ANDROID
            Debug.Log("Running on UNITY_ANDROID");
#elif UNITY_IOS
            Debug.Log("Running on UNITY_IOS");
#elif UNITY_WEBGL
            Debug.Log("Running on UNITY_WEBGL");
#else
            Console.WriteLine("Running outside of Unity");
#endif
        }
    }
}
