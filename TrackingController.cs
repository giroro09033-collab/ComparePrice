using Microsoft.AspNetCore.Mvc;

namespace PriceCompare.Api.Controllers
{
    public record TrackClickRequest(string Keyword, string Platform, string ProductUrl, decimal Price);

    /// <summary>
    /// 導購追蹤系統 (Tracking API)。
    /// 使用者點擊「前往購買」的當下，前端會呼叫這支 API 記錄一筆導購行為，
    /// 但絕對不能因為寫入 log 而拖慢使用者跳轉到購物網站的速度。
    /// 因此 API 本身只做輕量驗證，實際寫入交給訊息佇列 (Channel/Kafka) 非同步處理，
    /// Controller 立即回 202 Accepted，確保高併發點擊下延遲穩定、不掉單。
    /// </summary>
    [ApiController]
    [Route("api/track")]
    public class TrackingController : ControllerBase
    {
        private readonly ITrackingQueue _queue;
        private readonly ILogger<TrackingController> _logger;

        public TrackingController(ITrackingQueue queue, ILogger<TrackingController> logger)
        {
            _queue = queue;
            _logger = logger;
        }

        [HttpPost("click")]
        public IActionResult TrackClick([FromBody] TrackClickRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Platform) || string.IsNullOrWhiteSpace(request.Keyword))
                return BadRequest("Invalid tracking payload");

            // 只做入列動作，不等待落地完成，維持 API 回應在毫秒等級
            var accepted = _queue.TryEnqueue(new TrackingEvent
            {
                Keyword = request.Keyword,
                Platform = request.Platform,
                ProductUrl = request.ProductUrl,
                Price = request.Price,
                Timestamp = DateTime.UtcNow,
                ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            });

            if (!accepted)
            {
                // 佇列滿載時仍先讓使用者導購成功，僅記警告，避免影響轉換率
                _logger.LogWarning("Tracking queue is full, event dropped for {Platform}", request.Platform);
            }

            return Accepted();
        }
    }

    public class TrackingEvent
    {
        public string Keyword { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string ProductUrl { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public DateTime Timestamp { get; set; }
        public string ClientIp { get; set; } = string.Empty;
    }

    /// <summary>以 System.Threading.Channels 實作的有界佇列，Controller 端非阻塞入列。</summary>
    public interface ITrackingQueue
    {
        bool TryEnqueue(TrackingEvent evt);
        IAsyncEnumerable<TrackingEvent> ReadAllAsync(CancellationToken ct);
    }

    public class TrackingQueue : ITrackingQueue
    {
        private readonly System.Threading.Channels.Channel<TrackingEvent> _channel =
            System.Threading.Channels.Channel.CreateBounded<TrackingEvent>(
                new System.Threading.Channels.BoundedChannelOptions(5000)
                {
                    FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite
                });

        public bool TryEnqueue(TrackingEvent evt) => _channel.Writer.TryWrite(evt);

        public IAsyncEnumerable<TrackingEvent> ReadAllAsync(CancellationToken ct) =>
            _channel.Reader.ReadAllAsync(ct);
    }

    /// <summary>背景 Worker：批次消費佇列，寫入資料庫 / 分析管線，與 API 請求完全解耦。</summary>
    public class TrackingLogWorker : BackgroundService
    {
        private readonly ITrackingQueue _queue;
        private readonly ILogger<TrackingLogWorker> _logger;
        private readonly List<TrackingEvent> _buffer = new();

        public TrackingLogWorker(ITrackingQueue queue, ILogger<TrackingLogWorker> logger)
        {
            _queue = queue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var evt in _queue.ReadAllAsync(stoppingToken))
            {
                _buffer.Add(evt);
                if (_buffer.Count >= 100)
                {
                    // TODO: bulk insert 到資料庫或送進分析用資料湖
                    _logger.LogInformation("批次寫入 {Count} 筆導購紀錄", _buffer.Count);
                    _buffer.Clear();
                }
            }
        }
    }
}
