﻿using BackupWarden.Abstractions.Services.UI;
using BackupWarden.Core.ViewModels;
using BackupWarden.Views;
using Microsoft.UI.Xaml;
using System.Threading.Tasks;

namespace BackupWarden.Activation
{
    public class DefaultActivationHandler : ActivationHandler<LaunchActivatedEventArgs>
    {
        private readonly INavigationService _navigationService;

        public DefaultActivationHandler(INavigationService navigationService)
        {
            _navigationService = navigationService;
        }

        protected override bool CanHandleInternal(LaunchActivatedEventArgs args)
        {
            // None of the ActivationHandlers has handled the activation.
            return _navigationService.Frame?.Content == null;
        }

        protected async override Task HandleInternalAsync(LaunchActivatedEventArgs args)
        {
            _navigationService.NavigateTo(MainViewModel.PageKey, args.Arguments);

            await Task.CompletedTask;
        }
    }
}

