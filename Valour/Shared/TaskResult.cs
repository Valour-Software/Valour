using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Valour.Shared
{
    public class TaskResult
    {
        public string Response { get; set; }
        public bool Success { get; set; }

        public TaskResult(bool success, string response)
        {
            Success = success;
            Response = response;
        }

        public override string ToString()
        {
            if (Success)
            {
                return $"[SUCC] {Response}";
            }

            return $"[FAIL] {Response}";
        }
    }
}
