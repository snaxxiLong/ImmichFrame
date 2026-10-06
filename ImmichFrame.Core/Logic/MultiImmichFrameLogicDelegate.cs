using System.Collections.Frozen;
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Exceptions;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Models;
using Microsoft.Extensions.Logging;

namespace ImmichFrame.Core.Logic;

public class MultiImmichFrameLogicDelegate : IImmichFrameLogic, IDisposable
{
    private readonly FrozenDictionary<IAccountSettings, IAccountImmichFrameLogic> _accountToDelegate;
    private readonly IServerSettings _serverSettings;
    private readonly IAccountSelectionStrategy _accountSelectionStrategy;
    private readonly ILogger<MultiImmichFrameLogicDelegate> _logger;

    public MultiImmichFrameLogicDelegate(IServerSettings serverSettings,
        Func<IAccountSettings, IAccountImmichFrameLogic> logicFactory, ILogger<MultiImmichFrameLogicDelegate> logger,
        Func<IList<IAccountImmichFrameLogic>, IAccountSelectionStrategy> strategyFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serverSettings = serverSettings;
        _accountToDelegate = serverSettings.Accounts.ToFrozenDictionary(
            keySelector: a => a,
            elementSelector: logicFactory
        );
        _accountSelectionStrategy = strategyFactory(_accountToDelegate.Values.ToList());
    }

    public async Task<AssetResponseDto?> GetNextAsset() => (await _accountSelectionStrategy.GetNextAsset())?.ToAsset();


    public async Task<IEnumerable<AssetResponseDto>> GetAssets()
        => (await _accountSelectionStrategy.GetAssets()).Shuffle().Select(it => it.ToAsset());


    public Task<AssetResponseDto> GetAssetInfoById(Guid assetId)
        => ForAssetWithFallback(assetId, async logic => (await logic.GetAssetInfoById(assetId)).WithAccount(logic));

    public Task<IEnumerable<AssetFaceResponseDto>> GetAssetFacesById(Guid assetId)
        => ForAssetWithFallback(assetId, logic => logic.GetAssetFacesById(assetId));


    public Task<IEnumerable<AlbumResponseDto>> GetAlbumInfoById(Guid assetId)
        => ForAssetWithFallback(assetId, logic => logic.GetAlbumInfoById(assetId));


    public Task<AssetResponse> GetAsset(Guid assetId, AssetTypeEnum? assetType = null, string? rangeHeader = null)
        => ForAssetWithFallback(assetId, logic => logic.GetAsset(assetId, assetType, rangeHeader));

    public Task DeleteAsset(Guid assetId)
        => ForAssetWithFallback(assetId, async logic =>
        {
            await logic.DeleteAsset(assetId);
            return true;
        });

    private async Task<T> ForAssetWithFallback<T>(Guid assetId, Func<IAccountImmichFrameLogic, Task<T>> f)
    {
        try
        {
            return await _accountSelectionStrategy.ForAsset(assetId, f);
        }
        catch (AssetNotFoundException)
        {
            // The account tracker only knows assets served since the last restart, but a frame can still
            // show assets it fetched before. Fall back to the account that can see the asset.
        }

        foreach (var logic in _accountToDelegate.Values)
        {
            try
            {
                await logic.GetAssetInfoById(assetId);
            }
            catch (ApiException)
            {
                continue;
            }

            return await f(logic);
        }

        throw new AssetNotFoundException($"No account found for asset {assetId}");
    }

    public async Task<long> GetTotalAssets()
    {
        var allInts = await Task.WhenAll(_accountToDelegate.Values.Select(account => account.GetTotalAssets()));
        return allInts.Sum();
    }

    public Task SendWebhookNotification(IWebhookNotification notification) =>
        WebhookHelper.SendWebhookNotification(notification, _serverSettings.GeneralSettings.Webhook);

    public void Dispose()
    {
        foreach (var accountLogic in _accountToDelegate.Values)
        {
            (accountLogic as IDisposable)?.Dispose();
        }
    }
}

public static class AccountAndAssetExtensions
{
    public static AssetResponseDto ToAsset(this (IAccountImmichFrameLogic, AssetResponseDto) accountAndAsset)
    {
        var (account, asset) = accountAndAsset;
        return asset.WithAccount(account);
    }

    public static AssetResponseDto WithAccount(this AssetResponseDto asset, IAccountImmichFrameLogic account)
    {
        asset.ImmichServerUrl = account.AccountSettings.ImmichServerUrl;
        return asset;
    }
}