using AllMCPSolution.Repositories;

namespace AllMCPSolution.Tools;

public abstract class CountryToolBase : CrudToolBase
{
    protected CountryToolBase(ICountryRepository countryRepository)
    {
        CountryRepository = countryRepository;
    }

    protected ICountryRepository CountryRepository { get; }
}
