using PriceCompare.Api.Models;

namespace PriceCompare.Api.Services
{
    /// <summary>
    /// 非同步排程服務 (BackgroundService)。
    /// 三個平台的商品資料不會即時查詢，而是由背景排程定時（例如每 15 分鐘）
    /// 非同步抓取熱門關鍵字的最新價格、跑一次 ProductMatchingService 比對，
    /// 寫回資料庫並更新 Redis 快取。使用者請求永遠只讀快取或資料庫，
    /// 不必等待外部平台 API 的回應時間，這是延遲與資料庫負載雙重優化的關鍵設計。
    /// </summary>
    public class PriceSyncScheduler : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PriceSyncScheduler> _logger;
        private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(15);

        // 示範用熱門關鍵字清單，正式環境可改為依「搜尋頻率」動態排序
        private static readonly string[] TrackedKeywords = { "iPhone 16", "iPhone 16 Pro", "AirPods Pro", "MacBook Air" };

        public PriceSyncScheduler(IServiceScopeFactory scopeFactory, ILogger<PriceSyncScheduler> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // 三平台平行非同步抓取，互不阻塞，任一平台逾時不影響其他平台結果
                var tasks = TrackedKeywords.Select(keyword => SyncKeywordAsync(keyword, stoppingToken));
                await Task.WhenAll(tasks);

                await Task.Delay(SyncInterval, stoppingToken);
            }
        }

        private async Task SyncKeywordAsync(string keyword, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var matcher = scope.ServiceProvider.GetRequiredService<ProductMatchingService>();
            var cache = scope.ServiceProvider.GetRequiredService<PriceCacheService>();
            // var pchomeClient / shopeeClient / momoClient 從 scope 取得，各自呼叫平台 API/爬蟲

            try
            {
                _logger.LogInformation("開始同步關鍵字：{Keyword}", keyword);

                var rawResults = await FetchFromAllPlatformsAsync(keyword, ct);
                var groups = matcher.GroupProducts(rawResults);

                await cache.SetAsync(keyword, groups);
                // TODO: 同步寫入資料庫 (EF Core) 供歷史價格分析使用

                _logger.LogInformation("關鍵字「{Keyword}」同步完成，共 {Count} 組商品", keyword, groups.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步關鍵字「{Keyword}」失敗", keyword);
            }
        }

        private async Task<List<UnifiedProduct>> FetchFromAllPlatformsAsync(string keyword, CancellationToken ct)
        {
            // 示範：三平台的資料來源客戶端平行呼叫，實務上會各自實作對應的
            // IPlatformProductSource（PChomeSource / ShopeeSource / MomoSource）
            var results = new List<UnifiedProduct>();
            await Task.Delay(50, ct); // placeholder：實際會是 HttpClient 呼叫各平台 API
            return results;
        }
    }
}
