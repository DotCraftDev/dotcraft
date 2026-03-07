using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace DotCraft.WeCom;

/// <summary>
/// 企业微信消息加解密工具类
/// 参考: https://developer.work.weixin.qq.com/document/path/90930
/// </summary>
public class WeComBizMsgCrypt
{
    private readonly string _token;
    
    private readonly byte[] _aesKey;

    public WeComBizMsgCrypt(string token, string encodingAesKey)
    {
        _token = token ?? throw new ArgumentNullException(nameof(token));
        if (string.IsNullOrEmpty(encodingAesKey))
            throw new ArgumentNullException(nameof(encodingAesKey));

        _aesKey = Convert.FromBase64String(PadBase64(encodingAesKey));
    }

    /// <summary>
    /// 验证 URL（企业微信配置机器人时的验证请求）
    /// </summary>
    public string VerifyUrl(string msgSignature, string timestamp, string nonce, string echoStr)
    {
        // 1. 验证签名
        var signature = ComputeSignature(timestamp, nonce, echoStr);
        if (signature != msgSignature)
            throw new Exception($"签名验证失败: expected={msgSignature}, actual={signature}");

        // 2. 解密 echoStr
        var plainBytes = AesDecrypt(echoStr);
        var (_, msg, _) = ParsePlainText(plainBytes);

        return Encoding.UTF8.GetString(msg);
    }

    /// <summary>
    /// 解密消息（企业微信发来的回调消息）
    /// </summary>
    public string DecryptMsg(string msgSignature, string timestamp, string nonce, string postData)
    {
        // 1. 解析 XML 获取加密数据
        var doc = XDocument.Parse(postData);
        var encryptNode = doc.Root?.Element("Encrypt");
        if (encryptNode == null)
            throw new Exception("无法从 XML 中提取 Encrypt 节点");

        var encryptData = encryptNode.Value;

        // 2. 验证签名
        var signature = ComputeSignature(timestamp, nonce, encryptData);
        if (signature != msgSignature)
            throw new Exception($"签名验证失败: expected={msgSignature}, actual={signature}");

        // 3. 解密消息
        var plainBytes = AesDecrypt(encryptData);
        var (_, msg, _) = ParsePlainText(plainBytes);

        return Encoding.UTF8.GetString(msg);
    }

    /// <summary>
    /// 加密消息（回复给企业微信）
    /// </summary>
    public string EncryptMsg(string replyMsg, string timestamp, string nonce)
    {
        // 1. 生成随机字符串
        var random = GenerateRandomString(16);

        // 2. 构造明文：random(16字节) + msgLen(4字节) + msg + receiverId(可为空)
        var msgBytes = Encoding.UTF8.GetBytes(replyMsg);
        var msgLenBytes = BitConverter.GetBytes(msgBytes.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(msgLenBytes); // 转为大端序

        var plainText = new MemoryStream();
        plainText.Write(Encoding.UTF8.GetBytes(random));
        plainText.Write(msgLenBytes);
        plainText.Write(msgBytes);
        // receiverId 留空

        // 3. AES 加密
        var encrypted = AesEncrypt(plainText.ToArray());

        // 4. 计算签名
        var signature = ComputeSignature(timestamp, nonce, encrypted);

        // 5. 构造返回的 XML
        var responseXml = new XElement("xml",
            new XElement("Encrypt", new XCData(encrypted)),
            new XElement("MsgSignature", new XCData(signature)),
            new XElement("TimeStamp", timestamp),
            new XElement("Nonce", new XCData(nonce))
        );

        return responseXml.ToString();
    }

    /// <summary>
    /// 计算签名：SHA1(sort(token, timestamp, nonce, encrypt))
    /// </summary>
    private string ComputeSignature(string timestamp, string nonce, string encrypt)
    {
        var arr = new[] { _token, timestamp, nonce, encrypt };
        Array.Sort(arr, StringComparer.Ordinal);
        var raw = string.Concat(arr);

        using var sha1 = SHA1.Create();
        var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }

    /// <summary>
    /// 企业微信自定义 PKCS7 填充的 block size（与 Go SDK 一致，为 32 而非 AES 标准的 16）
    /// </summary>
    private const int Pkcs7BlockSize = 32;

    /// <summary>
    /// AES 加密（CBC 模式，手动 PKCS7 填充 block_size=32）
    /// </summary>
    private string AesEncrypt(byte[] plainText)
    {
        // 手动 PKCS7 padding (block_size=32)
        var padded = Pkcs7Pad(plainText, Pkcs7BlockSize);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None; // 手动处理 padding
        aes.Key = _aesKey;
        aes.IV = _aesKey[..16];

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(padded, 0, padded.Length);
        return Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// AES 解密（CBC 模式，手动 PKCS7 去填充 block_size=32）
    /// </summary>
    private byte[] AesDecrypt(string base64EncryptedText)
    {
        var encryptedBytes = Convert.FromBase64String(base64EncryptedText);

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None; // 手动处理 padding
        aes.Key = _aesKey;
        aes.IV = _aesKey[..16];

        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

        // 手动 PKCS7 unpadding (block_size=32)
        return Pkcs7Unpad(decrypted, Pkcs7BlockSize);
    }

    /// <summary>
    /// PKCS7 填充（自定义 block size）
    /// </summary>
    private static byte[] Pkcs7Pad(byte[] data, int blockSize)
    {
        var padding = blockSize - (data.Length % blockSize);
        var padBytes = new byte[padding];
        Array.Fill(padBytes, (byte)padding);
        var result = new byte[data.Length + padding];
        Buffer.BlockCopy(data, 0, result, 0, data.Length);
        Buffer.BlockCopy(padBytes, 0, result, data.Length, padding);
        return result;
    }

    /// <summary>
    /// PKCS7 去填充（自定义 block size）
    /// </summary>
    private static byte[] Pkcs7Unpad(byte[] data, int blockSize)
    {
        if (data.Length == 0)
            throw new Exception("PKCS7 去填充失败: 数据为空");
        if (data.Length % blockSize != 0)
            throw new Exception("PKCS7 去填充失败: 数据长度不是 block size 的倍数");

        var paddingLen = data[^1];
        if (paddingLen < 1 || paddingLen > blockSize)
            throw new Exception($"PKCS7 去填充失败: 无效的填充长度 {paddingLen}");

        return data[..^paddingLen];
    }

    /// <summary>
    /// 解析明文：random(16) + msgLen(4) + msg + receiverId
    /// </summary>
    private static (byte[] random, byte[] msg, byte[] receiverId) ParsePlainText(byte[] plainText)
    {
        if (plainText.Length < 20)
            throw new Exception("解密后的数据长度不足");

        var random = plainText[..16];
        var msgLenBytes = plainText[16..20];

        // 大端序读取消息长度
        var msgLen = (int)((msgLenBytes[0] << 24) | (msgLenBytes[1] << 16) | (msgLenBytes[2] << 8) | msgLenBytes[3]);

        if (plainText.Length < 20 + msgLen)
            throw new Exception("消息长度不匹配");

        var msg = plainText[20..(20 + msgLen)];
        var receiverId = plainText[(20 + msgLen)..];

        return (random, msg, receiverId);
    }

    /// <summary>
    /// 补齐 Base64 填充字符，支持可变长度的 EncodingAESKey
    /// </summary>
    private static string PadBase64(string base64)
    {
        var remainder = base64.Length % 4;
        return remainder switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64
        };
    }

    /// <summary>
    /// 生成随机字符串
    /// </summary>
    private static string GenerateRandomString(int length)
    {
        const string chars = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var random = new Random();
        return new string(Enumerable.Range(0, length)
            .Select(_ => chars[random.Next(chars.Length)])
            .ToArray());
    }
}
