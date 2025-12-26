using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Services;
using UltimateVideoBrowser.ViewModels;
using Xunit;

namespace UltimateVideoBrowser.Tests;

public sealed class SourcesViewModelTests
{
    [Fact]
    public async Task AddSourceAsync_DoesNotAddWhenPromptCancelled()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        try
        {
            var sourceService = new FakeSourceService();
            var folderPicker = new FakeFolderPickerService(new FolderPickResult(tempPath, "Videos"));
            var dialogService = new FakeDialogService(null);
            var viewModel = new SourcesViewModel(
                sourceService,
                new PermissionService(),
                folderPicker,
                dialogService);

            await viewModel.AddSourceAsync();

            Assert.Empty(sourceService.Sources);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    private sealed class FakeFolderPickerService : IFolderPickerService
    {
        private readonly FolderPickResult? result;

        public FakeFolderPickerService(FolderPickResult? result)
        {
            this.result = result;
        }

        public Task<IReadOnlyList<FolderPickResult>> PickFoldersAsync(CancellationToken ct = default)
        {
            return Task.FromResult<IReadOnlyList<FolderPickResult>>(
                result == null ? Array.Empty<FolderPickResult>() : new[] { result });
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        private readonly string? promptResult;

        public FakeDialogService(string? promptResult)
        {
            this.promptResult = promptResult;
        }

        public Task DisplayAlertAsync(string title, string message, string cancel)
        {
            return Task.CompletedTask;
        }

        public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
        {
            return Task.FromResult(false);
        }

        public Task<string?> DisplayPromptAsync(
            string title,
            string message,
            string accept,
            string cancel,
            string? placeholder,
            int maxLength,
            Keyboard keyboard,
            string? initialValue = null)
        {
            return Task.FromResult(promptResult);
        }
    }

    private sealed class FakeSourceService : ISourceService
    {
        public List<MediaSource> Sources { get; } = new();

        public Task<List<MediaSource>> GetSourcesAsync()
        {
            return Task.FromResult(Sources.OrderBy(s => s.DisplayName).ToList());
        }

        public Task EnsureDefaultSourceAsync()
        {
            return Task.CompletedTask;
        }

        public Task UpsertAsync(MediaSource src)
        {
            Sources.RemoveAll(s => s.Id == src.Id);
            Sources.Add(src);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(MediaSource src)
        {
            Sources.RemoveAll(s => s.Id == src.Id);
            return Task.CompletedTask;
        }
    }
}
