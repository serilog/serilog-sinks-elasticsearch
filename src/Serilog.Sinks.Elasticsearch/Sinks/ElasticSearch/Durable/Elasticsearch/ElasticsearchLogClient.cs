using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Elasticsearch.Net;
using Serilog.Debugging;

namespace Serilog.Sinks.Elasticsearch.Durable
{
    /// <summary>
    /// 
    /// </summary>
    public class ElasticsearchLogClient : ILogClient<List<string>>
    {
        private readonly IElasticLowLevelClient _elasticLowLevelClient;
        private readonly Func<string, long?, string, string> _cleanPayload;
        private readonly ElasticOpType _elasticOpType;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="elasticLowLevelClient"></param>
        /// <param name="cleanPayload"></param>
        /// <param name="elasticOpType"></param>
        public ElasticsearchLogClient(IElasticLowLevelClient elasticLowLevelClient,
            Func<string, long?, string, string> cleanPayload,
            ElasticOpType elasticOpType)
        {
            _elasticLowLevelClient = elasticLowLevelClient;
            _cleanPayload = cleanPayload;
            _elasticOpType = elasticOpType;
        }

        public async Task<SentPayloadResult> SendPayloadAsync(List<string> payload)
        {
            return await SendPayloadAsync(payload, true);
        }

        public async Task<SentPayloadResult> SendPayloadAsync(List<string> payload, bool first)
        {
            try
            {
                if (payload == null || !payload.Any()) return new SentPayloadResult(null, true);
                var response = await _elasticLowLevelClient.BulkAsync<DynamicResponse>(PostData.MultiJson(payload));

                if (response.Success)
                {
                    var cleanPayload = new List<string>();
                    var invalidPayload = GetInvalidPayloadAsync(response, payload, out cleanPayload);
                    if ((cleanPayload?.Any() ?? false) && first)
                    {
                        return await SendPayloadAsync(cleanPayload, false);
                    }

                    return new SentPayloadResult(response, true, invalidPayload);
                }
                else
                {
                    SelfLog.WriteLine("Received failed ElasticSearch shipping result {0}: {1}", response.HttpStatusCode,
                        response.OriginalException);
                    return new SentPayloadResult(response, false,
                        new InvalidResult()
                        {
                            StatusCode = response.HttpStatusCode ?? 500,
                            Content = response.OriginalException.ToString()
                        });
                }
            }
            catch (Exception ex)
            {
                SelfLog.WriteLine("Exception while emitting periodic batch from {0}: {1}", this, ex);
                return new SentPayloadResult(null, false, null, ex);
            }


        }

        private InvalidResult GetInvalidPayloadAsync(DynamicResponse baseResult, List<string> payload, out List<string> cleanPayload)
        {
            int i = 0;
            cleanPayload = new List<string>();
            var items = baseResult.Body["items"];
            if (items == null) return null;
            List<string> badPayload = new List<string>();

            bool hasErrors = false;
            foreach (dynamic item in items)
            {
                var itemIndex = item?[ElasticsearchSink.BulkAction(_elasticOpType)];
                long? status = itemIndex?["status"];
                i++;
                if (!status.HasValue || status < 300)
                {
                    continue;
                }

                hasErrors = true;
                var id = itemIndex?["_id"];
                var error = itemIndex?["error"];
                var errorString = $"type: {error?["type"] ?? "Unknown"}, reason: {error?["reason"] ?? "Unknown"}";

                SelfLog.WriteLine($"Received failed ElasticSearch shipping result {status}: {errorString}.");
                if (int.TryParse(id.Split('_')[0], out int index))
                {
                    badPayload.Add(payload.ElementAt(index * 2));
                    badPayload.Add(payload.ElementAt(index * 2 + 1));
                    if (_cleanPayload != null)
                    {
                        cleanPayload.Add(payload.ElementAt(index * 2));
                        cleanPayload.Add(_cleanPayload(payload.ElementAt(index * 2 + 1), status, errorString));
                    }
                }
            }

            if (!hasErrors)
                return null;
            return new InvalidResult()
            {
                StatusCode = baseResult.HttpStatusCode ?? 500,
                Content = baseResult.ToString(),
                BadPayLoad = String.Join(Environment.NewLine, badPayload)
            };
        }
    }
}
