namespace PriceCompare.Api.Models
{
    /// <summary>
    /// 統一商品資料結構 (Unified Data Schema)。
    /// 三個電商平台（PChome / 蝦皮 / momo）的原始資料格式各不相同，
    /// 皆會在 Ingestion 階段被轉換成這個共用 Schema，後續比對、快取、
    /// 排序、去重都只需處理這一種結構，不必再理會來源平台的差異。
    /// </summary>
    public class UnifiedProduct
    {
        public string Id { get; set; } = string.Empty;          // 內部去重後的商品群組 ID
        public string SourcePlatform { get; set; } = string.Empty; // pchome / shopee / momo
        public string SourceProductId { get; set; } = string.Empty; // 各平台原始商品編號
        public string RawTitle { get; set; } = string.Empty;      // 平台原始標題
        public string NormalizedTitle { get; set; } = string.Empty; // 正規化後標題（供比對用）
        public string Brand { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;         // 例如 iPhone 16
        public string? Variant { get; set; }                      // 容量 / 顏色，例如 128GB 藍色
        public decimal Price { get; set; }
        public string Currency { get; set; } = "TWD";
        public string ProductUrl { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public bool InStock { get; set; }
        public DateTime FetchedAt { get; set; }

        /// <summary>比對信心分數 0~100，由 ProductMatchingService 計算並寫回。</summary>
        public double MatchConfidence { get; set; }

        /// <summary>同一實體商品在三平台的所有來源，比對成功後會被歸類到同一個 MatchedGroup。</summary>
        public string? MatchedGroupKey { get; set; }
    }

    /// <summary>
    /// 比對後的商品群組：同一件商品在三個平台的價格集合，
    /// 前端「三平台比價卡片」直接對應這個物件。
    /// </summary>
    public class MatchedProductGroup
    {
        public string GroupKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<UnifiedProduct> Offers { get; set; } = new();
        public decimal LowestPrice => Offers.Count == 0 ? 0 : Offers.Min(o => o.Price);
    }
}
