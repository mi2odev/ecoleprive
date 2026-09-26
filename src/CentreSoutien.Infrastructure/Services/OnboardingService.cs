using CentreSoutien.Application.Abstractions;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

/// <summary>Computes the "Premiers pas" checklist with a few cheap existence queries.</summary>
public sealed class OnboardingService(IDbContextFactory<AppDbContext> factory) : IOnboardingService
{
    public async Task<OnboardingStatus> GetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct);
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(ct);
        var hasSubjects = await db.Subjects.AnyAsync(ct);
        var hasRooms = await db.Rooms.AnyAsync(ct);
        var hasTeachers = await db.Teachers.AnyAsync(ct);
        var hasCourses = await db.Groups.AnyAsync(ct);
        var hasScheduledGroup = await db.Slots.AnyAsync(ct);
        var hasStudents = await db.Students.AnyAsync(ct);
        var hasEnrollments = await db.Enrollments.AnyAsync(ct);

        var centerInfo = settings is not null
            && !string.IsNullOrWhiteSpace(settings.CenterName)
            && !string.IsNullOrWhiteSpace(settings.Address)
            && !string.IsNullOrWhiteSpace(settings.Phone);

        IReadOnlyList<OnboardingItem> steps =
        [
            new(OnboardingStep.CenterInfo, "Informations du centre renseignées", "Nom, adresse et téléphone : ils apparaissent sur les reçus et les bulletins.", centerInfo),
            new(OnboardingStep.Logo, "Logo personnalisé", "Facultatif : votre logo sur les reçus et documents imprimés.", !string.IsNullOrEmpty(settings?.LogoFile), IsOptional: true),
            new(OnboardingStep.Password, "Mot de passe personnel", "Remplacez le mot de passe par défaut par le vôtre.", account is { MustChangePassword: false }),
            new(OnboardingStep.Subjects, "Matières créées", "Mathématiques, Physique, Français…", hasSubjects),
            new(OnboardingStep.Rooms, "Salles créées", "Les salles de cours et leur capacité.", hasRooms),
            new(OnboardingStep.Teachers, "Enseignants ajoutés", "Avec leur mode de rémunération.", hasTeachers),
            new(OnboardingStep.CoursesAndGroups, "Groupes créés", "Au moins un groupe (matière, niveau, prix) avec un créneau dans l'emploi du temps.", hasCourses && hasScheduledGroup),
            new(OnboardingStep.Students, "Élèves inscrits", "Ajoutez-les un par un ou importez-les depuis Excel, puis inscrivez-les dans un groupe.", hasEnrollments),
            new(OnboardingStep.Backup, "Première sauvegarde effectuée", "Une copie chiffrée de vos données, à garder aussi sur une clé USB.", settings?.LastBackupAt is not null),
        ];
        return new OnboardingStatus(steps, !hasStudents && !hasTeachers && !hasCourses);
    }
}
