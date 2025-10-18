using AllMCPSolution.Repositories;

namespace AllMCPSolution.Tools;

public abstract class RegionToolBase : CrudToolBase
{
    protected RegionToolBase(IRegionRepository regionRepository, ICountryRepository countryRepository)
    {
        RegionRepository = regionRepository;
        CountryRepository = countryRepository;
    }

    protected IRegionRepository RegionRepository { get; }
    protected ICountryRepository CountryRepository { get; }
}
