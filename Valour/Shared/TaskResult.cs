using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared
{
    public struct TaskResult
    {
        [JsonInclude]
        [JsonPropertyName("Message")]
        public string Message { get; set; }

        [JsonInclude]
        [JsonPropertyName("Success")]
        public bool Success { get; set; }
        
        public TaskResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public override string ToString()
        {
            if (Success)
            {
                return $"[SUCC] {Message}";
            }

            return $"[FAIL] {Message}";
        }

    }

    public struct HttpResult
    {
        [JsonInclude]
        [JsonPropertyName("Message")]
        public string Message { get; set; }

        [JsonInclude]
        [JsonPropertyName("Status")]
        public int Status { get; set; }

        public bool Success
        {
            get
            {
                int x = Status - 200;
                if (x < 0) return false;
                if (x > 99) return false;
                return true;
            }
        }

        public HttpResult(string message, int status)
        {
            Message = message;
            Status = status;
        }

        public override string ToString()
        {
            return $"[{Status}]: {Message}";
        }
    }

    public struct HttpResult<T>
    {
        [JsonInclude]
        [JsonPropertyName("Message")]
        public string Message { get; set; }

        [JsonInclude]
        [JsonPropertyName("Status")]
        public int Status { get; set; }


        [JsonInclude]
        [JsonPropertyName("Result")]
        public T Result { get; set; }

        public bool Success
        {
            get
            {
                int x = Status - 200;
                if (x < 0) return false;
                if (x > 99) return false;
                return true;
            }
        }

        public HttpResult(string message, int status, T result)
        {
            Message = message;
            Status = status;
            Result = result;
        }

        public override string ToString()
        {
            return $"[{Status}]: {Message}";
        }
    }

    public struct TaskResult<T>
    {
        [JsonInclude]
        [JsonPropertyName("Message")]
        public string Message { get; set; }

        [JsonInclude]
        [JsonPropertyName("Success")]
        public bool Success { get; set; }

        [JsonInclude]
        [JsonPropertyName("Data")]
        public T Data { get; set; }

        public TaskResult(bool success, string message)
        {
            Success = success;
            Message = message;
            Data = default(T);
        }

        public TaskResult(bool success, string message, T data)
        {
            Success = success;
            Message = message;
            Data = data;
        }

        public override string ToString()
        {
            if (Success)
            {
                return $"[SUCC] {Message}";
            }

            return $"[FAIL] {Message}";
        }
    }
}
