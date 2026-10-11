using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ReconstructNodesServer.Utility
{
    public static class StringCompressor
    {
        /// <summary>
        /// 高效能压缩字符串
        /// </summary>
        /// <param name="text">要压缩的字符串</param>
        /// <returns>压缩后的 Base64 字符串</returns>
        public static string Compress(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            byte[] buffer = Encoding.UTF8.GetBytes(text);
            int originalLength = buffer.Length;

            using (var memoryStream = new MemoryStream(originalLength / 2)) // 预估压缩后大小
            {
                // 写入原始长度 (便于解压时精确分配内存)
                memoryStream.Write(BitConverter.GetBytes(originalLength), 0, 4);

                // 使用最佳压缩级别
                using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, true))
                {
                    gzipStream.Write(buffer, 0, buffer.Length);
                }

                // 直接使用内部缓冲区进行Base64编码，避免重复分配内存
                byte[] compressedData = memoryStream.GetBuffer();
                int compressedLength = (int)memoryStream.Length;

                // 计算Base64编码后的长度并预分配内存
                int base64Length = ((compressedLength + 2) / 3) * 4;
                char[] base64Chars = ArrayPool<char>.Shared.Rent(base64Length);

                try
                {
                    // 执行Base64编码
                    int bytesWritten = Convert.ToBase64CharArray(compressedData, 0, compressedLength, base64Chars, 0);
                    return new string(base64Chars, 0, bytesWritten);
                }
                finally
                {
                    // 归还租借的数组
                    ArrayPool<char>.Shared.Return(base64Chars);
                }
            }
        }

        /// <summary>
        /// 高效能解压缩字符串
        /// </summary>
        /// <param name="compressedText">Base64 格式的压缩字符串</param>
        /// <returns>解压缩后的原始字符串</returns>
        public static string Decompress(string compressedText)
        {
            if (string.IsNullOrEmpty(compressedText))
                return string.Empty;

            // 计算Base64解码后的数据大小
            int compressedBytesLength = (compressedText.Length * 3) / 4;
            if (compressedText.EndsWith("==")) compressedBytesLength -= 2;
            else if (compressedText.EndsWith("=")) compressedBytesLength -= 1;

            // 从池中租借缓冲区而不是分配新内存
            byte[] compressedBytes = ArrayPool<byte>.Shared.Rent(compressedBytesLength);

            try
            {
                // 正确解码Base64数据
                int decodedBytes;
                try
                {
                    // 使用标准的Convert.FromBase64String
                    byte[] tempBuffer = Convert.FromBase64String(compressedText);
                    decodedBytes = Math.Min(tempBuffer.Length, compressedBytes.Length);
                    Buffer.BlockCopy(tempBuffer, 0, compressedBytes, 0, decodedBytes);
                }
                catch (FormatException)
                {
                    throw new FormatException("Invalid Base64 string");
                }

                using (var memoryStream = new MemoryStream(compressedBytes, 0, decodedBytes))
                {
                    // 读取原始数据长度
                    byte[] lengthBytes = new byte[4];
                    if (memoryStream.Read(lengthBytes, 0, 4) != 4)
                        throw new InvalidDataException("Compressed data corrupted: missing length prefix");

                    int originalLength = BitConverter.ToInt32(lengthBytes, 0);
                    if (originalLength < 0)
                        throw new InvalidDataException("Invalid original data length");

                    // 从池中租借结果缓冲区
                    byte[] resultBuffer = ArrayPool<byte>.Shared.Rent(originalLength);

                    try
                    {
                        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                        {
                            int totalBytesRead = 0;
                            int bytesRead;

                            // 循环读取确保所有数据都被读取
                            while (totalBytesRead < originalLength &&
                                  (bytesRead = gzipStream.Read(resultBuffer, totalBytesRead, originalLength - totalBytesRead)) > 0)
                            {
                                totalBytesRead += bytesRead;
                            }

                            if (totalBytesRead != originalLength)
                                throw new InvalidDataException($"Decompressed data size ({totalBytesRead}) doesn't match expected size ({originalLength})");

                            // 使用租借的缓冲区直接创建字符串，避免额外复制
                            return Encoding.UTF8.GetString(resultBuffer, 0, originalLength);
                        }
                    }
                    finally
                    {
                        // 归还租借的结果缓冲区
                        ArrayPool<byte>.Shared.Return(resultBuffer);
                    }
                }
            }
            finally
            {
                // 归还租借的压缩数据缓冲区
                ArrayPool<byte>.Shared.Return(compressedBytes);
            }
        }
    }
}
