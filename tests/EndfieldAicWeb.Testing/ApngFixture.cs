namespace EndfieldAicWeb.Testing;

/// <summary>
/// テスト仕様書 docs/phases/test-specification-phase8.md の I-03（実際の APNG）フィクスチャ。
/// Pillow で生成した 3 フレーム 80×80 の APNG（遅延 400/800/400ms、acTL チャンクを含む）。
/// </summary>
public static class ApngFixture
{
    private const string ApngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAFAAAABQCAYAAACPEfKtAAAACGFjVEwAAAADAAAAAM7tusAAAAAaZmNUTAAAAAAAAABQAAAAUAAA" +
        "AAAAAAAAAAIABQAAqEllnQAAAY5JREFUeJztnDtuw0AMBdc5gyufNEfISV35DknFRgig5T5yvzO1IfGNn6hG2FIAAAAA4EQeowcw" +
        "3t/PX8/vXz+fKWYfNoRX2B2jhHa/abS4K71FdrtZtrgrvUSm36S3uCvZItMuPlrclSyRXxkXnU1eKXkzhQucUZ6RMVtYrWcW9x9R" +
        "j3RIA1eTV0rczLLAFeUZEbOnvEROQhK4cvsMNUOzwB3kGUqWJoE7yTNaM7EDRdwCd2yf0ZKNBoq4BO7cPsObkQaKVAs8oX2GJysN" +
        "FKkSeFL7jNrMNFAEgSIIFLkVeOL+M2qy00ARBIogUASBIggUQaAIAkUQKIJAEQSKIFDkVuAsX8OPoCY7DRRBoAgCRaoEnrgHazPT" +
        "QJFqgSe10JOVBoq4BJ7QQm9GGijiFrhzC1uy0UCRJoE7trA1U3MDd5KoZJEe4R0kqhnYgSKywJVbGDF7SANXlBg1c3jw2b/miv6z" +
        "w3fgzG3MmC3lJTKjxKyZOPZEhIN3RDj6SYTDx0SmWfarHn8HAAAAAGfyB+dElHS7ogYDAAAAGmZjVEwAAAABAAAAQAAAAEAAAAAI" +
        "AAAACAAEAAUAAGJ2cY4AAAFdZmRBVAAAAAJ4nO2bsRXCQAxDD2agYlJGYFIqdoDqSiC2Jcu8J7Xg+Funu6RITqtZt8vj9e33+/N6" +
        "6mJZa62WZr+G/qQOM87sBtnhq7VHRXMYDc9KAyUBjJVjpQFuADO2jGtDDejYs+geMAM6hmf0ghjQOTy6Z9kAxfDI3vTngOkqGaBc" +
        "fRSDE5AtnLD6WxUWJyBTNGn1t7JMTkC0YOLqb2XYnAA1gFohAybHfyvK6ASoAdSyAWoAtQ4b8A8H4FaE1QlQA6hlA9QAatkANYBa" +
        "NkANoJYNUAOoZQPUAGrZgKN/7H59raIIqxOgBlDLBqgB1AoZ8A8HYZTRCVADqBU2YPI2yLA5AZmiiSnIMjkB2cJJKaiwOAGV4gkp" +
        "qDI4AdULKFOA6A1JgMIEVE/YFug0AdkLegZ0mIDuAT8EmSYwrk25CzBAWcbSI1t9t4i9rejPAZUBOs6U9tvXtM/n35TIfHw8oS54" +
        "AAAAGmZjVEwAAAADAAAALAAAAEAAAAASAAAACAACAAUAAMe8QdMAAADvZmRBVAAAAAR4nO2aQQ6DMAwETd/QZ/FcntU/0BNSxaWx" +
        "vUsie+eGRFajlROQYDMw7+M4f68/+74h82Fhd9E7KPEXIuSf7Og9I0CEnyQt7GkO0XJKOCKQle4zEpmmMmt7NIzYPNGMHg3PxC2M" +
        "emJFs+o3PBsJs3EJIzdcNLN2wysgYTZ1hRknRCS7bsOrIGE2EmYjYTYSZiNhNhJmI2E2dYXRHwij2XUbXgUJs6ktzDgpvJm1G14B" +
        "CbNxCyM3XiSrfsOzCQkjxiKa0aNhs1zLmbV9GjaLNZWd/3TDHgHEZu01EhcjzaGekPD3W/bvuF93xkx+CDwPUQAAAABJRU5ErkJg" +
        "gg==";

    /// <summary>I-03 の実 APNG バイト列。</summary>
    public static readonly byte[] ApngBytes = Convert.FromBase64String(ApngBase64);

    /// <summary>フィクスチャが acTL チャンクを含む APNG であることの自己検証。</summary>
    public static bool HasActlChunk()
    {
        byte[] bytes = ApngBytes;
        for (int p = 8; p + 8 <= bytes.Length;)
        {
            int length =
                (bytes[p] << 24) | (bytes[p + 1] << 16) | (bytes[p + 2] << 8) | bytes[p + 3];
            string type = System.Text.Encoding.ASCII.GetString(bytes, p + 4, 4);
            if (type == "acTL")
            {
                return true;
            }

            if (type == "IEND")
            {
                return false;
            }

            p += 12 + length;
        }

        return false;
    }
}
