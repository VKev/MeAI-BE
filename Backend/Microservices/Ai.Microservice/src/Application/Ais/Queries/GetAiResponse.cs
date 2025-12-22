using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Application.Ais.Queries
{
    public sealed record GetAiResponse(
        string Fullname,
        string Email
    );
}