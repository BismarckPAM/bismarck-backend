using AutoMapper;
using Identity.Service.DTOs;
using Identity.Service.Models;

namespace Identity.Service.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<CreateUserRequest, User>();
        CreateMap<UpdateUserRequest, User>()
            .ForMember(user => user.IsActive, options => options.MapFrom(request => request.IsActive!.Value));
        CreateMap<User, UserResponse>()
            .ForMember(response => response.Role, options => options.MapFrom(user => user.Role.Name))
            .ForMember(response => response.Department, options => options.MapFrom(user => user.Department.Name));
    }
}
