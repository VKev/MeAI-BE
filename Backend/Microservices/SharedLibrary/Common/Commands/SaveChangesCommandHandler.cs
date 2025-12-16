using System.Threading;
using System.Threading.Tasks;
using MediatR;
using SharedLibrary.Abstractions.UnitOfWork;
using SharedLibrary.Common.ResponseModel;

namespace SharedLibrary.Common.Commands
{
    public sealed class SaveChangesCommandHandler : IRequestHandler<SaveChangesCommand, Result>
    {
        private readonly ISaveChangesUnitOfWork _unitOfWork;

        public SaveChangesCommandHandler(ISaveChangesUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<Result> Handle(SaveChangesCommand request, CancellationToken cancellationToken)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
    }
}


