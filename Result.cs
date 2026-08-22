using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace kv_store
{
    public class Result(ErrorCode _err)
    {
        public ErrorCode Error = _err;
    }

    // class Result<T>(Error _err) : Result(_err)
    // {
    //     public T? result;
    // }
    // commented out result payload on purpose because I prefer the return error / payload in out parameter pattern more for now.
}
