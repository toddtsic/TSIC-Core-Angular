using TSIC.Contracts.Dtos.Store;

namespace TSIC.Contracts.Services;

public interface IStoreWalkUpService
{
	Task<StoreWalkUpRegisterResponse> RegisterAsync(StoreWalkUpRegisterRequest request);
}
