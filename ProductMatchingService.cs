using PriceCompare.Api.Models;

namespace PriceCompare.Api.Services
{
    /// <summary>
    /// 關鍵字權重比對演算法。
    /// 三平台的商品標題寫法差異很大，例如：
    ///   PChome：「Apple iPhone 16 128G 藍色」
    ///   蝦皮  ：「【現貨】iPhone16 128GB 藍 全新未拆」
    ///   momo  ：「Apple iPhone 16(128G)-藍色 5G智慧型手機」
    /// 本服務將標題切詞後，依「品牌 / 型號 / 容量 / 顏色 / 干擾詞」給予不同權重，
    /// 計算兩兩標題的相似分數，超過門檻即視為同一商品並歸類到同一 MatchedProductGroup。
    /// </summary>
    public class ProductMatchingService
    {
        // 各類關鍵字權重：型號與容量是判斷「是不是同一商品」最關鍵的欄位，權重最高。
        private static readonly Dictionary<string, double> FieldWeights = new()
        {
            { "Brand", 0.15 },
            { "Model", 0.45 },
            { "Capacity", 0.25 },
            { "Color", 0.15 },
        };

        // 訂單/行銷用詞不影響商品本質，比對前先移除，避免干擾相似度計算。
        private static readonly string[] NoiseWords =
        {
            "現貨", "全新", "未拆", "公司貨", "原廠", "限量", "熱賣", "免運",
            "smartphone", "智慧型手機", "5g"
        };

        public string Normalize(string rawTitle)
        {
            var text = rawTitle.ToLowerInvariant();
            foreach (var noise in NoiseWords)
                text = text.Replace(noise.ToLowerInvariant(), " ");

            text = System.Text.RegularExpressions.Regex.Replace(text, @"[\[\]【】()（）\-_/]", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }

        private (string Brand, string Model, string Capacity, string Color) ExtractFields(string normalizedTitle)
        {
            var brand = normalizedTitle.Contains("apple") ? "apple" : "";
            var modelMatch = System.Text.RegularExpressions.Regex.Match(normalizedTitle, @"iphone\s?16(\s?pro)?");
            var capacityMatch = System.Text.RegularExpressions.Regex.Match(normalizedTitle, @"(\d{2,4})\s?g(b)?");
            var colorMatch = System.Text.RegularExpressions.Regex.Match(normalizedTitle, @"(藍|黑|白|粉|綠|紫|黃|鈦)色?");

            return (
                Brand: brand,
                Model: modelMatch.Success ? modelMatch.Value.Replace(" ", "") : "",
                Capacity: capacityMatch.Success ? capacityMatch.Groups[1].Value + "g" : "",
                Color: colorMatch.Success ? colorMatch.Value : ""
            );
        }

        /// <summary>計算兩個商品標題的加權相似分數（0~100）。</summary>
        public double CalculateSimilarity(string titleA, string titleB)
        {
            var a = ExtractFields(Normalize(titleA));
            var b = ExtractFields(Normalize(titleB));

            double score = 0;
            score += (a.Brand == b.Brand && a.Brand != "") ? FieldWeights["Brand"] : 0;
            score += (a.Model == b.Model && a.Model != "") ? FieldWeights["Model"] : 0;
            score += (a.Capacity == b.Capacity && a.Capacity != "") ? FieldWeights["Capacity"] : 0;
            score += (a.Color == b.Color && a.Color != "") ? FieldWeights["Color"] : 0;

            return Math.Round(score * 100, 1);
        }

        /// <summary>
        /// 將三平台撈回來的原始商品去重、歸類成 MatchedProductGroup 清單。
        /// 採貪婪分組：以相似度門檻 (預設 70 分) 判斷是否併入既有群組。
        /// </summary>
        public List<MatchedProductGroup> GroupProducts(IEnumerable<UnifiedProduct> rawProducts, double threshold = 70)
        {
            var groups = new List<MatchedProductGroup>();

            foreach (var product in rawProducts)
            {
                product.NormalizedTitle = Normalize(product.RawTitle);

                MatchedProductGroup? bestGroup = null;
                double bestScore = 0;

                foreach (var group in groups)
                {
                    var representative = group.Offers.First().RawTitle;
                    var score = CalculateSimilarity(representative, product.RawTitle);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestGroup = group;
                    }
                }

                if (bestGroup != null && bestScore >= threshold)
                {
                    product.MatchConfidence = bestScore;
                    product.MatchedGroupKey = bestGroup.GroupKey;
                    bestGroup.Offers.Add(product);
                }
                else
                {
                    var newGroup = new MatchedProductGroup
                    {
                        GroupKey = Guid.NewGuid().ToString("N"),
                        DisplayName = product.RawTitle,
                    };
                    product.MatchConfidence = 100;
                    product.MatchedGroupKey = newGroup.GroupKey;
                    newGroup.Offers.Add(product);
                    groups.Add(newGroup);
                }
            }

            return groups;
        }
    }
}
