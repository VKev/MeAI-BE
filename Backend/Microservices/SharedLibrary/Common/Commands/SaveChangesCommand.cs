using System.Threading;
using System.Threading.Tasks;
using MediatR;
using SharedLibrary.Common.ResponseModel;

namespace SharedLibrary.Common.Commands
{
    public sealed record SaveChangesCommand() : IRequest<Result>;
}


