using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SharedLibrary.Common.ResponseModel;
using SharedLibrary.Abstractions.Messaging;
using AutoMapper;
using Domain.Repositories;
using MediatR;

namespace Application.Ais.Queries
{
    public sealed record GetAllAisQuery : IQuery<IEnumerable<GetAiResponse>>;
    internal sealed class GetAllAisQueryHandler : IQueryHandler<GetAllAisQuery, IEnumerable<GetAiResponse>>
    {
        private readonly IAiRepository _aiRepository;
        private readonly IMapper _mapper;

        public GetAllAisQueryHandler(IAiRepository aiRepository, IMapper mapper)
        {
            _aiRepository = aiRepository;
            _mapper = mapper;
        }


        public async Task<Result<IEnumerable<GetAiResponse>>> Handle(GetAllAisQuery request, CancellationToken cancellationToken)
        {
            var users = await _aiRepository.GetAllAsync(cancellationToken);
            var userResponses = _mapper.Map<IEnumerable<GetAiResponse>>(users);
            return Result.Success(userResponses);
        }
    }
}