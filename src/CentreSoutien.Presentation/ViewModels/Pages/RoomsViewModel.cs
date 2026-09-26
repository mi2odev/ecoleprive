using CentreSoutien.Application.Abstractions;
using CentreSoutien.Application.Models;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Presentation.Core;
using CentreSoutien.Presentation.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Presentation.ViewModels.Pages;

public sealed record RoomRow(int Id, string Name, string Capacity, string Equipment, Badge Now, string Week, Badge Status, IRelayCommand Edit, IRelayCommand Delete);

public sealed partial class RoomsViewModel(
    ICrudService<Room> rooms, IScheduleService schedule, DialogHost dialogs, INotifier notifier, TimeProvider clock, IServiceProvider services) : PageViewModel
{
    public override string NavKey => "rooms";
    public override string Title => "Salles";

    [ObservableProperty] private IReadOnlyList<RoomRow> _rows = [];
    [ObservableProperty] private string _countLabel = "";
    [ObservableProperty] private string _nowLabel = "";

    public override async Task LoadAsync(object? parameter)
    {
        await RunAsync(async () =>
        {
            var now = clock.GetLocalNow().DateTime;
            NowLabel = $"État à {Labels.Time(now.TimeOfDay)}";
            var list = await rooms.ListAsync();
            var availability = (await schedule.RoomAvailabilityAsync(now)).ToDictionary(a => a.Room.Id);
            var week = await schedule.WeekAsync();
            Rows = list.Select(r =>
            {
                var slots = week.Where(s => (s.RoomId ?? s.Group!.RoomId) == r.Id).ToList();
                var hours = slots.Sum(s => (s.End - s.Start).TotalHours);
                var weekLabel = slots.Count == 0 ? "—" : $"{slots.Count} créneau{(slots.Count > 1 ? "x" : "")} · {hours:0.#} h";
                return new RoomRow(r.Id, r.Name, $"{r.Capacity} places", string.IsNullOrWhiteSpace(r.Equipment) ? "—" : r.Equipment,
                    NowBadge(availability.GetValueOrDefault(r.Id)), weekLabel, Badge.Active(r.IsActive),
                    new AsyncRelayCommand(() => EditAsync(r.Id)),
                    new AsyncRelayCommand(() => DeleteAsync(r)));
            }).ToList();
            var active = list.Count(r => r.IsActive);
            var free = availability.Values.Count(a => a.IsFree);
            CountLabel = $"{list.Count} salle{(list.Count > 1 ? "s" : "")} · {free} libre{(free > 1 ? "s" : "")} sur {active} en ce moment";
        }, notifier);
    }

    public static Badge NowBadge(RoomAvailability? a) => a switch
    {
        null => new Badge("—", BadgeKind.Neutral),
        { IsFree: true, FreeUntil: { } until } => new Badge($"Libre (jusqu'à {Labels.Time(until)})", BadgeKind.Ok),
        { IsFree: true } => new Badge("Libre", BadgeKind.Ok),
        _ => new Badge($"Occupée · {a.OccupiedBy}{(a.FreeUntil is { } end ? " jusqu'à " + Labels.Time(end) : "")}", BadgeKind.Warn),
    };

    [RelayCommand]
    private Task Add() => EditAsync(null);

    private async Task EditAsync(int? id)
    {
        var dialog = services.GetRequiredService<RoomEditorDialogViewModel>();
        if (!await RunAsync(() => dialog.InitializeAsync(id), notifier)) return;
        if (await dialogs.ShowAsync(dialog))
        {
            notifier.Info(id is null ? "Salle ajoutée" : "Salle enregistrée");
            await RefreshAsync();
        }
    }

    private async Task DeleteAsync(Room r)
    {
        if (!await dialogs.ConfirmAsync("Supprimer la salle", $"Supprimer « {r.Name} » ? Si elle est encore utilisée, rendez-la plutôt inactive.")) return;
        if (await RunAsync(() => rooms.DeleteAsync(r.Id), notifier))
        {
            notifier.Info("Salle supprimée");
            await RefreshAsync();
        }
    }
}
