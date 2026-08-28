using AutoMapper;
using Resource.Service.DTOs;
using Resource.Service.Models;
using ResourceModel = Resource.Service.Models.Resource;

namespace Resource.Service.Mappings;

public class MappingProfile : Profile
{
	public MappingProfile()
	{
		CreateMap<CreateResourceRequest, ResourceModel>();
		CreateMap<ResourceModel, ResourceResponse>();
	}
}
