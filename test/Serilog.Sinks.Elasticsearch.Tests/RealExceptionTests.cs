﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Elasticsearch.Net;
using FakeItEasy;
using FluentAssertions;
using NUnit.Framework;
using Serilog.Events;
using Serilog.Parsing;
using Serilog.Sinks.Elasticsearch.Tests.Serializer;
using Xunit;

namespace Serilog.Sinks.Elasticsearch.Tests
{
    public class RealExceptionTests : ElasticsearchSinkTestsBase
    {
        [Fact]
        public async Task WhenPassingASerializer_ShouldExpandToJson()
        {
            try
            {
                await new HttpClient().GetStringAsync("http://i.do.not.exist");
            }
            catch (Exception e)
            {
                var timestamp = new DateTimeOffset(2013, 05, 28, 22, 10, 20, 666, TimeSpan.FromHours(10));
                var messageTemplate = "{Song}++";
                var template = new MessageTemplateParser().Parse(messageTemplate);
                _options.Serializer = new ElasticsearchJsonNetSerializer();
                using (var sink = new ElasticsearchSink(_options))
                {
                    var properties = new List<LogEventProperty>
                    {
                        new LogEventProperty("Song", new ScalarValue("New Macabre")), 
                        new LogEventProperty("Complex", new ScalarValue(new { A  = 1, B = 2}))
                    };
                    var logEvent = new LogEvent(timestamp, LogEventLevel.Information, null, template, properties);
                    sink.Emit(logEvent);
                    logEvent = new LogEvent(timestamp.AddDays(2), LogEventLevel.Information, e, template, properties);
                    sink.Emit(logEvent);
                }

                A.CallTo(() => _connection.Request<Stream>(A<RequestData>.Ignored)).MustHaveHappened();
                A.CallTo(() => _connection.RequestAsync<Stream>(A<RequestData>.Ignored)).MustHaveHappened();

                _seenHttpPosts.Should().NotBeEmpty().And.HaveCount(1);
                var json = _seenHttpPosts.First();
                var bulkJsonPieces = json.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                bulkJsonPieces.Should().HaveCount(4);
                bulkJsonPieces[0].Should().Contain(@"""_index"":""logstash-2013.05.28");
                bulkJsonPieces[1].Should().Contain("New Macabre");
                bulkJsonPieces[1].Should().NotContain("Properties\"");
                bulkJsonPieces[2].Should().Contain(@"""_index"":""logstash-2013.05.30");

                //since we pass a serializer objects should serialize as json object and not using their
                //tostring implemenation
                //DO NOTE that you cant send objects as scalar values through Logger.*("{Scalar}", {});
                bulkJsonPieces[3].Should().Contain("Complex\":{");
                bulkJsonPieces[3].Should().Contain("exceptions\":[{");
            }
        }
    }

}
