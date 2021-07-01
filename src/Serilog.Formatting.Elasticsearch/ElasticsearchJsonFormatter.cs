// Copyright 2014 Serilog Contributors
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
#if !NO_SERIALIZATION
using System.Runtime.Serialization;
#endif
using Serilog.Events;
using Serilog.Parsing;

namespace Serilog.Formatting.Elasticsearch
{
    /// <summary>
    /// Custom Json formatter that respects the configured property name handling and forces 'Timestamp' to @timestamp
    /// </summary>
    public class ElasticsearchJsonFormatter : DefaultJsonFormatter
    {
        readonly ISerializer _serializer;
        readonly bool _inlineFields;
        readonly bool _formatStackTraceAsArray;

        /// <summary>
        /// Render message property name
        /// </summary>
        public const string RenderedMessagePropertyName = "message";
        /// <summary>
        /// Message template property name
        /// </summary>
        public const string MessageTemplatePropertyName = "messageTemplate";
        /// <summary>
        /// Exception property name
        /// </summary>
        public const string ExceptionPropertyName = "Exception";
        /// <summary>
        /// Level property name
        /// </summary>
        public const string LevelPropertyName = "level";
        /// <summary>
        /// Timestamp property name
        /// </summary>
        public const string TimestampPropertyName = "@timestamp";

        /// <summary>
        /// Construct a <see cref="ElasticsearchJsonFormatter"/>.
        /// </summary>
        /// <param name="omitEnclosingObject">If true, the properties of the event will be written to
        /// the output without enclosing braces. Otherwise, if false, each event will be written as a well-formed
        /// JSON object.</param>
        /// <param name="closingDelimiter">A string that will be written after each log event is formatted.
        /// If null, <see cref="Environment.NewLine"/> will be used. Ignored if <paramref name="omitEnclosingObject"/>
        /// is true.</param>
        /// <param name="renderMessage">If true, the message will be rendered and written to the output as a
        /// property named RenderedMessage.</param>
        /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
        /// <param name="serializer">Inject a serializer to force objects to be serialized over being ToString()</param>
        /// <param name="inlineFields">When set to true values will be written at the root of the json document</param>
        /// <param name="renderMessageTemplate">If true, the message template will be rendered and written to the output as a
        /// property named RenderedMessageTemplate.</param>
        /// <param name="formatStackTraceAsArray">If true, splits the StackTrace by new line and writes it as a an array of strings</param>
        public ElasticsearchJsonFormatter(
            bool omitEnclosingObject = false,
            string closingDelimiter = null,
            bool renderMessage = true,
            IFormatProvider formatProvider = null,
            ISerializer serializer = null,
            bool inlineFields = false,
            bool renderMessageTemplate = true,
            bool formatStackTraceAsArray = false)
            : base(omitEnclosingObject, closingDelimiter, renderMessage, formatProvider, renderMessageTemplate)
        {
            _serializer = serializer;
            _inlineFields = inlineFields;
            _formatStackTraceAsArray = formatStackTraceAsArray;
        }

        /// <summary>
        /// Writes out individual renderings of attached properties
        /// </summary>
        protected override void WriteRenderings(IGrouping<string, PropertyToken>[] tokensWithFormat, IReadOnlyDictionary<string, LogEventPropertyValue> properties, TextWriter output)
        {
            output.Write(",\"{0}\":{{", "renderings");
            WriteRenderingsValues(tokensWithFormat, properties, output);
            output.Write("}");
        }

        /// <summary>
        /// Writes out the attached properties
        /// </summary>
        protected override void WriteProperties(IReadOnlyDictionary<string, LogEventPropertyValue> properties, TextWriter output)
        {
            if (!_inlineFields)
                output.Write(",\"{0}\":{{", "fields");
            else
                output.Write(",");

            WritePropertiesValues(properties, output);

            if (!_inlineFields)
                output.Write("}");
        }

        /// <summary>
        /// Writes out the attached exception
        /// </summary>
        protected override void WriteException(Exception exception, ref string delim, TextWriter output)
        {
            output.Write(delim);
            output.Write("\"");
            output.Write("exceptions");
            output.Write("\":[");

            delim = "";
            this.WriteExceptionSerializationInfo(exception, ref delim, output, depth: 0);
            output.Write("]");
        }

        private void WriteExceptionSerializationInfo(Exception exception, ref string delim, TextWriter output, int depth)
        {
            while (true)
            {
                output.Write(delim);
                output.Write("{");
                delim = "";
                WriteSingleException(exception, ref delim, output, depth);
                output.Write("}");

                delim = ",";
                if (exception.InnerException != null && depth < 20)
                {
                    exception = exception.InnerException;
                    depth = ++depth;
                    continue;
                }

                break;
            }
        }

        /// <summary>
        /// Writes the properties of a single exception, without inner exceptions
        /// Callers are expected to open and close the json object themselves.
        /// </summary>
        /// <param name="exception"></param>
        /// <param name="delim"></param>
        /// <param name="output"></param>
        /// <param name="depth"></param>
        protected void WriteSingleException(Exception exception, ref string delim, TextWriter output, int depth)
        {
#if NO_SERIALIZATION
            var helpUrl = exception.HelpLink;
            var stackTrace = exception.StackTrace;
            var remoteStackTrace = string.Empty;
            var remoteStackIndex = -1;
            var exceptionMethod = string.Empty;
            var hresult = exception.HResult;
            var source = exception.Source;
            var className = string.Empty;

#else
            var si = new SerializationInfo(exception.GetType(), new FormatterConverter());
            var sc = new StreamingContext();
            exception.GetObjectData(si, sc);

            var helpUrl = si.GetString("HelpURL");
            var stackTrace = si.GetString("StackTraceString");
            var remoteStackTrace = si.GetString("RemoteStackTraceString");
            var remoteStackIndex = si.GetInt32("RemoteStackIndex");
            var exceptionMethod = si.GetString("ExceptionMethod");
            var hresult = si.GetInt32("HResult");
            var source = si.GetString("Source");
            var className = si.GetString("ClassName");
#endif

            //TODO Loop over ISerializable data


            this.WriteJsonProperty("Depth", depth, ref delim, output);
            this.WriteJsonProperty("ClassName", className, ref delim, output);
            this.WriteJsonProperty("Message", exception.Message, ref delim, output);
            this.WriteJsonProperty("Source", source, ref delim, output);
            if (_formatStackTraceAsArray)
            {
                this.WriteMultilineString("StackTrace", stackTrace, ref delim, output);
                this.WriteMultilineString("RemoteStackTrace", remoteStackTrace, ref delim, output);
            }
            else
            {
                this.WriteJsonProperty("StackTraceString", stackTrace, ref delim, output);
                this.WriteJsonProperty("RemoteStackTraceString", remoteStackTrace, ref delim, output);
            }
            this.WriteJsonProperty("RemoteStackIndex", remoteStackIndex, ref delim, output);
            this.WriteStructuredExceptionMethod(exceptionMethod, ref delim, output);
            this.WriteJsonProperty("HResult", hresult, ref delim, output);
            this.WriteJsonProperty("HelpURL", helpUrl, ref delim, output);

            //writing byte[] will fall back to serializer and they differ in output 
            //JsonNET assumes string, simplejson writes array of numerics.
            //Skip for now
            //this.WriteJsonProperty("WatsonBuckets", watsonBuckets, ref delim, output);
        }

        private void WriteMultilineString(string name, string value, ref string delimeter, TextWriter output)
        {
            var lines = value?.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries) ?? new string[] { };
            WriteJsonArrayProperty(name, lines, ref delimeter, output);
        }

        private void WriteStructuredExceptionMethod(string exceptionMethodString, ref string delim, TextWriter output)
        {
            if (string.IsNullOrWhiteSpace(exceptionMethodString)) return;

            var args = exceptionMethodString.Split('\0', '\n');

            if (args.Length != 5) return;

            var memberType = Int32.Parse(args[0], CultureInfo.InvariantCulture);
            var name = args[1];
            var assemblyName = args[2];
            var className = args[3];
            var signature = args[4];
            var an = new AssemblyName(assemblyName);
            output.Write(delim);
            output.Write("\"");
            output.Write("ExceptionMethod");
            output.Write("\":{");
            delim = "";
            this.WriteJsonProperty("Name", name, ref delim, output);
            this.WriteJsonProperty("AssemblyName", an.Name, ref delim, output);
            this.WriteJsonProperty("AssemblyVersion", an.Version.ToString(), ref delim, output);
            this.WriteJsonProperty("AssemblyCulture", an.CultureName, ref delim, output);
            this.WriteJsonProperty("ClassName", className, ref delim, output);
            this.WriteJsonProperty("Signature", signature, ref delim, output);
            this.WriteJsonProperty("MemberType", memberType, ref delim, output);

            output.Write("}");
            delim = ",";
        }

        /// <summary>
        /// (Optionally) writes out the rendered message
        /// </summary>
        protected override void WriteRenderedMessage(string message, ref string delim, TextWriter output)
        {
            WriteJsonProperty(RenderedMessagePropertyName, message, ref delim, output);
        }

        /// <summary>
        /// Writes out the message template for the logevent.
        /// </summary>
        protected override void WriteMessageTemplate(string template, ref string delim, TextWriter output)
        {
            WriteJsonProperty(MessageTemplatePropertyName, template, ref delim, output);
        }

        /// <summary>
        /// Writes out the log level
        /// </summary>
        protected override void WriteLevel(LogEventLevel level, ref string delim, TextWriter output)
        {
            var stringLevel = Enum.GetName(typeof(LogEventLevel), level);
            WriteJsonProperty(LevelPropertyName, stringLevel, ref delim, output);
        }

        /// <summary>
        /// Writes out the log timestamp
        /// </summary>
        protected override void WriteTimestamp(DateTimeOffset timestamp, ref string delim, TextWriter output)
        {
            WriteJsonProperty(TimestampPropertyName, timestamp, ref delim, output);
        }

        /// <summary>
        /// Allows a subclass to write out objects that have no configured literal writer.
        /// </summary>
        /// <param name="value">The value to be written as a json construct</param>
        /// <param name="output">The writer to write on</param>
        protected override void WriteLiteralValue(object value, TextWriter output)
        {
            if (_serializer != null)
            {
                string jsonString = _serializer.SerializeToString(value);
                output.Write(jsonString);
                return;
            }

            base.WriteLiteralValue(value, output);
        }

    }
}
