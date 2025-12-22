using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Application.Ais.Commands;
using Application.Ais.Queries;
using AutoMapper;
using Domain.Entities;

namespace Application.Common.Mapper
{
    public class AutoMapperProfile : Profile
    {
        public AutoMapperProfile()
        {
            CreateMap<CreateAiCommand, Ai>()
                .ConstructUsing(src => Ai.Create(src.Fullname, src.Email, src.PhoneNumber));

            CreateMap<Ai, GetAiResponse>();
        }
        
    }
}
