using System;
using System.Linq;
using Prism.Mvvm;
using Prism.Navigation.Regions;
using TheAirline.Infrastructure;
using TheAirline.Models.General;

namespace TheAirline.ViewModels.Game
{
    public class PageAirlineDataViewModel : BindableBase, INavigationAware
    {
        private Player _player;
        private readonly AppState _state;

        public PageAirlineDataViewModel(AppState state)
        {
            _state = state;
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            _player = (from player in _state.Context.Players
                where
                    player.Id == (int) navigationContext.Parameters["player"]
                select player).FirstOrDefault();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext)
        {
            throw new NotImplementedException();
        }

        public void OnNavigatedFrom(NavigationContext navigationContext)
        {
            throw new NotImplementedException();
        }
    }
}