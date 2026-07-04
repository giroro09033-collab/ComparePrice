using Microsoft.AspNetCore.Mvc;
using PriceCompare.Api.Models;
using PriceCompare.Api.Services;

namespace PriceCompare.Api.Controllers
{
    /// <summary>
    /// 比價查詢 API。前端輸入關鍵字（例如「iPhone 16」）呼叫此端點，
    /// 回傳已去重、比對完成的 MatchedProductGroup 清單，直接對應前端的比價卡片。
    /// </summary>
    [ApiController]
    [Route("api/prices")]
    public class PriceController : ControllerBase
    {
        private readonly PriceCacheService _cache;
        private readonly ProductMatchingService _matcher;

        public PriceController(PriceCacheService cache, ProductMatchingService matcher)
        {
            _cache = cache;
            _matcher = matcher;
        }

        [HttpGet("search")]
        public async Task<ActionResult<List<MatchedProductGroup>>> Search([FromQuery] string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return BadRequest("keyword is required");

            // 1. 優先讀 Redis 快取（多數熱門關鍵字查詢在此命中，免打資料庫）
            var cached = await _cache.GetAsync(keyword);
            if (cached != null)
                return Ok(cached);

            // 2. 快取未命中 -> 查資料庫既有的最新一次同步結果
            //    （實際抓取三平台是由 PriceSyncScheduler 背景排程負責，
            //     API 本身不即時呼叫外部平台，避免使用者等待外部延遲）
            var groups = await LoadFromDatabaseAsync(keyword);

            if (groups.Count > 0)
                await _cache.SetAsync(keyword, groups);

            return Ok(groups);
        }

        private Task<List<MatchedProductGroup>> LoadFromDatabaseAsync(string keyword)
        {
            // TODO: 實際串接 EF Core / Dapper 查詢資料庫最新同步結果
            return Task.FromResult(new List<MatchedProductGroup>());
        }
    }
}
