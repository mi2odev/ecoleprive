using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Infrastructure.Data;
using CentreSoutien.Infrastructure.Security;
using CentreSoutien.Infrastructure.Services;
using CentreSoutien.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CentreSoutien.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the local (SQLite, single computer) implementation of every application service.</summary>
    public static IServiceCollection AddLocalInfrastructure(this IServiceCollection services, StorageOptions storage, IKeyProtector? protector = null)
    {
        services.AddSingleton(storage);
        services.AddSingleton<AppPaths>();
        services.AddSingleton(protector ?? DefaultProtector());
        services.AddSingleton<KeyStore>();
        services.AddSingleton<ConnectionStringProvider>();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContextFactory<AppDbContext>((sp, o) =>
        {
            ConnectionStringProvider.InitializeSqlite();
            o.UseSqlite(sp.GetRequiredService<ConnectionStringProvider>().ConnectionString);
        });

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<AppSession>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<IFileStorage, FileStorage>();
        services.AddSingleton<ISystemInfo, SystemInfo>();

        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IStudentService, StudentService>();
        services.AddSingleton<IParentService, ParentService>();
        services.AddSingleton<ITeacherService, TeacherService>();
        services.AddSingleton<ICourseService, CourseService>();
        services.AddSingleton<IGroupService, GroupService>();
        services.AddSingleton<IScheduleService, ScheduleService>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IAttendanceService, AttendanceService>();
        services.AddSingleton<IExamService, ExamService>();
        services.AddSingleton<IPaymentService, PaymentService>();
        services.AddSingleton<ITeacherPaymentService, TeacherPaymentService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<IInsightsService, InsightsService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IDocumentService, DocumentService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IDemoDataService, DemoDataService>();
        services.AddSingleton<ICrudService<Subject>, SubjectService>();
        services.AddSingleton<ICrudService<Room>, RoomService>();
        services.AddSingleton<ICrudService<Discount>, DiscountService>();
        services.AddSingleton<ICrudService<Expense>, ExpenseService>();
        return services;
    }

    private static IKeyProtector DefaultProtector() =>
        OperatingSystem.IsWindows() ? new DpapiKeyProtector() : new PlainKeyProtector();
}
