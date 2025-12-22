using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SharedLibrary.Contracts.UserCreating
{
    public class AiCreatedFailureEvent
    {
        public Guid CorrelationId {get; set;}

        public string Reason {get; set;} = null!;
    }
}
