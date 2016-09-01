using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;

using Gelf;

using NLog.Config;

namespace NLog.Targets.Gelf
{
    [Target("Gelf")]
    public sealed class GelfTarget : Target
    {
        #region Private Members

        const int ShortMessageLength = 250;
        const int MaxMessageIdSize = 8;

        #endregion

        #region Public properties

        [RequiredParameter]
        public string GelfServer { get; set; }

        public int Port { get; set; }
        public string Facility { get; set; }

        #endregion

        #region Public Constructors

        public GelfTarget()
        {
            GelfServer = "127.0.0.1";
            Port = 12201;
            Facility = null;
        }

        #endregion

        #region Overridden NLog methods

        /// <summary>
        /// This is where we hook into NLog, by overriding the Write method. 
        /// </summary>
        /// <param name="logEvent">The NLog.LogEventInfo </param>
        protected override void Write(LogEventInfo logEvent)
        {
            try
            {
                SendMessage(GelfServer, Port, CreateGelfJsonFromLoggingEvent(logEvent));
            }
            catch (Exception exception)
            {
                // If there's an error then log the message.
                SendMessage(GelfServer, Port, CreateFatalGelfJson(exception));
            }
        }

        #endregion

        #region Private Methods

        private void SendMessage(string gelfServer, int serverPort, GelfMessage message)
        {
            var publisher = new GelfPublisher(gelfServer, serverPort);
            publisher.Publish(message);
        }

        private static byte[] GzipMessage(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            var stream = new MemoryStream();

            using (var gZipStream = new GZipStream(stream, CompressionMode.Compress, true))
            {
                gZipStream.Write(buffer, 0, buffer.Length);
            }
            stream.Position = 0;

            var compressed = new byte[stream.Length];
            stream.Read(compressed, 0, compressed.Length);

            return compressed;
        }

        private GelfMessage CreateGelfJsonFromLoggingEvent(LogEventInfo logEventInfo)
        {
            var shortMessage = logEventInfo.FormattedMessage.Length > ShortMessageLength ? logEventInfo.FormattedMessage.Substring(0, ShortMessageLength - 1) : logEventInfo.FormattedMessage;

            var gelfMessage = new GelfMessage
                {
                    Facility = Facility ?? "GELF",
                    FullMessage = logEventInfo.FormattedMessage,
                    Host = Dns.GetHostName(),
                    Level = logEventInfo.Level.GelfSeverity(),
                    ShortMessage = shortMessage
                };

            if (!string.IsNullOrWhiteSpace(logEventInfo.LoggerName)) gelfMessage.Add("Logger", logEventInfo.LoggerName);

            if (logEventInfo.Properties != null)
            {
                object notes;
                if (logEventInfo.Properties.TryGetValue("Notes", out notes))
                {
                    gelfMessage.Add("Notes", notes);
                }
            }

            if (logEventInfo.Exception == null) return gelfMessage;

            var exceptioToLog = logEventInfo.Exception;

            gelfMessage.Add("ExceptionType", exceptioToLog.GetType().Name);
            gelfMessage.Add("ExceptionMessage", exceptioToLog.Message);
            gelfMessage.Add("Exception", exceptioToLog.ToString());

            return gelfMessage;
        }

        private GelfMessage CreateFatalGelfJson(Exception exception)
        {
            var gelfMessage = new GelfMessage
                {
                    Facility = Facility ?? "GELF",
                    FullMessage = "Error sending message in NLog.Targets.Gelf",
                    Host = Dns.GetHostName(),
                    Level = LogLevel.Fatal.GelfSeverity(),
                    ShortMessage = "Error sending message in NLog.Targets.Gelf"
                };

            if (exception == null) return gelfMessage;

            var exceptioToLog = exception;

            gelfMessage.Add("ExceptionType", exceptioToLog.GetType().Name);
            gelfMessage.Add("ExceptionMessage", exceptioToLog.Message);
            gelfMessage.Add("Exception", exceptioToLog.ToString());

            return gelfMessage;
        }

        private static byte[] CreateChunkedMessagePart(string messageId, int chunkNumber, int chunkCount)
        {
            //Chunked GELF ID: 0x1e 0x0f (identifying this message as a chunked GELF message)
            var result = new List<byte>
                {
                    Convert.ToByte(30),
                    Convert.ToByte(15)
                };

            //Message ID: 32 bytes
            result.AddRange(Encoding.Default.GetBytes(messageId));

            result.AddRange(GetChunkPart(chunkNumber, chunkCount));

            return result.ToArray<byte>();
        }

        private static IEnumerable<byte> GetChunkPart(int chunkNumber, int chunkCount)
        {
            return new List<byte>
                {
                    Convert.ToByte(chunkNumber),
                    Convert.ToByte(chunkCount)
                };
        }

        private static string GenerateMessageId()
        {
            var random = new Random((int) DateTime.Now.Ticks);
            var r = random.Next(10000000).ToString("00000000");

            //Message ID: 8 bytes
            return r.Substring(0, MaxMessageIdSize);
        }

        #endregion
    }
}
