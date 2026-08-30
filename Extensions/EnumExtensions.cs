using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using kv_store.Enums;

namespace kv_store.Extensions
{
    public static class EnumExtensions
    {
        // Not very sure about this it's copy pasted.
        public static string GetDescription(this ErrorCode value)
        {
            var field = value.GetType().GetField(value.ToString());
            var attribute = field?.GetCustomAttribute<DescriptionAttribute>(false);
            return attribute?.Description ?? $"Unknown Error Occured";
        }
    }
}
