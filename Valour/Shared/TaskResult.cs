using System.Text.Json.Serialization;

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Shared
{
    public struct TaskResult : ITaskResult
    {
        public static readonly TaskResult SuccessResult = new(true, "Success");

        [JsonInclude]
        [JsonPropertyName("Message")]
        public string Message { get; set; }
        
        [JsonInclude]
        [JsonPropertyName("Details")]
        public string Details { get; set; }
        
        [JsonInclude]
        [JsonPropertyName("ErrorCode")]
        public int? Code { get; set; }

        [JsonInclude]
        [JsonPropertyName("Success")]
        public bool Success { get; set; }

        public TaskResult(bool success, string message, string details = null, int? errorCode = null)
        {
            Success = success;
            Message = message;
            Details = details;
            Code = errorCode;
        }

        public static TaskResult FromFailure(ITaskResult failure) => new(false, failure.Message, failure.Details, failure.Code);

        public static TaskResult FromFailure(string message, int? errorCode = null) => new(false, message, errorCode: errorCode);
        public static TaskResult FromFailure(Exception ex, int? errorCode = null) => new(false, ex.Message, ex.StackTrace, errorCode);

        public static TaskResult FromSuccess(string message) => new(true, message);
        
        public override string ToString()
        {
            if (Success)
            {
                return $"[SUCC] {Message}";
            }

            return $"[FAIL ({Code})] {Message}";
        }

    }
    public struct TaskResult<T> : ITaskResult
    {
        [JsonInclude]
        [JsonPropertyName("Message")]
        public string Message { get; set; }
        
        [JsonInclude]
        [JsonPropertyName("Details")]
        public string Details { get; set; }
        
        [JsonInclude]
        [JsonPropertyName("ErrorCode")]
        public int? Code { get; set; }

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
        }

        public TaskResult(bool success, string message, T data, string details = null, int? code = null)
        {
            Success = success;
            Message = message;
            Data = data;
            Details = details;
            Code = code;
        }

        public static TaskResult<T> FromData(T data) => new(true, "Success", data);

        public static TaskResult<T> FromFailure(ITaskResult failure) => new(false, failure.Message, default(T), failure.Details, failure.Code);

        public static TaskResult<T> FromFailure(string message) => new(false, message, default(T), null, code: null);
        public static TaskResult<T> FromFailure(string message, int? code, string details) => new(false, message, default(T), details, code: code);
        public static TaskResult<T> FromFailure(string message, int? code) => new(false, message, default(T), null, code: code);
        public static TaskResult<T> FromFailure(Exception ex, int? code = null) => new(false, ex.Message, default(T), ex.StackTrace, code: code);

        public bool IsSuccessful(out T value)
        {
            value = Data;
            return Success;
        }
        
        public TaskResult WithoutData() => new(Success, Message, Details, Code);

        public override string ToString()
        {
            if (Success)
            {
                return $"[SUCC] {Message}";
            }

            return $"[FAIL] {Message}";
        }
    }

    public interface ITaskResult
    {
        string Message { get; set; }
        
        string Details { get; set; }
        
        int? Code { get; set; }

        bool Success { get; set; }
    }
}
