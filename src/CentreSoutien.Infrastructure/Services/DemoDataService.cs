using CentreSoutien.Application.Abstractions;
using CentreSoutien.Domain.Calculations;
using CentreSoutien.Domain.Entities;
using CentreSoutien.Domain.Enums;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Services;

/// <summary>Sample data modelled on the design prototype: 9 teachers, 11 groups, 50 students, a month of payments.</summary>
public sealed class DemoDataService(IDbContextFactory<AppDbContext> factory, TimeProvider clock) : IDemoDataService
{
    public async Task<bool> IsDatabaseEmptyAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return !await db.Students.AnyAsync(ct) && !await db.Teachers.AnyAsync(ct) && !await db.Groups.AnyAsync(ct);
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!await IsDatabaseEmptyAsync(ct)) throw new BusinessException("La base contient déjà des données.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var seed = 11;
        double R() => (seed = (int)(seed * 16807L % 2147483647)) / 2147483647.0;
        T Pick<T>(IReadOnlyList<T> a) => a[(int)(R() * a.Count)];
        var today = clock.GetLocalNow().Date;
        var period = Period.Of(today);

        var subjects = new[] { ("Mathématiques", "Maths"), ("Physique", "Physique"), ("Français", "Français"), ("Anglais", "Anglais"),
            ("Sciences naturelles", "Sciences"), ("Philosophie", "Philo"), ("Arabe", "Arabe"), ("Informatique", "Info"), ("Histoire-géographie", "Histoire") }
            .Select(x => new Subject { Name = x.Item1, ShortName = x.Item2 }).ToList();
        db.Subjects.AddRange(subjects);
        Subject S(string n) => subjects.First(s => s.Name == n);

        var rooms = new[] { ("Salle 1", 16), ("Salle 2", 16), ("Salle 3", 16), ("Salle 4", 18), ("Labo", 10), ("Salle Info", 8) }
            .Select(x => new Room { Name = x.Item1, Capacity = x.Item2 }).ToList();
        db.Rooms.AddRange(rooms);
        Room Rm(string n) => rooms.First(r => r.Name == n);

        var teachers = new (string F, string L, string Subj, CompensationType T, decimal V, string Phone, int Since)[]
        {
            ("Karima", "Boudiaf", "Mathématiques", CompensationType.Percentage, 40, "0661 24 18 90", 2018),
            ("Sofiane", "Hadjadj", "Physique", CompensationType.Percentage, 40, "0770 53 21 07", 2019),
            ("Nadia", "Merabet", "Français", CompensationType.PerSession, 2000, "0555 84 12 66", 2020),
            ("Omar", "Belhadj", "Anglais", CompensationType.PerSession, 2000, "0662 90 47 13", 2021),
            ("Leïla", "Saadi", "Sciences naturelles", CompensationType.Percentage, 35, "0771 12 88 40", 2020),
            ("Hakim", "Zitouni", "Philosophie", CompensationType.PerSession, 2200, "0560 31 75 92", 2022),
            ("Fatima", "Kerroum", "Arabe", CompensationType.PerSession, 1800, "0698 45 03 21", 2019),
            ("Redouane", "Aït Ahmed", "Informatique", CompensationType.FixedMonthly, 25000, "0775 60 14 38", 2023),
            ("Samir", "Lakhdar", "Histoire-géographie", CompensationType.PerSession, 1800, "0551 72 30 19", 2021),
        }.Select((t, i) => new Teacher
        {
            FirstName = t.F, LastName = t.L, Subject = S(t.Subj), CompensationType = t.T, CompensationValue = t.V, Phone = t.Phone,
            StartYear = t.Since, IsActive = i != 8,
        }).ToList();
        db.Teachers.AddRange(teachers);
        Teacher Tc(string last) => teachers.First(t => t.LastName == last);

        var sat = DayOfWeek.Saturday; var sun = DayOfWeek.Sunday; var mon = DayOfWeek.Monday; var tue = DayOfWeek.Tuesday; var wed = DayOfWeek.Wednesday; var thu = DayOfWeek.Thursday;
        var groupDefs = new (string Subj, string Level, string Name, string Teacher, string Room, decimal Price, int Cap, (DayOfWeek, int, int)[] Slots)[]
        {
            ("Mathématiques", "3AS", "A", "Boudiaf", "Salle 1", 4500, 12, [(sat, 9, 11), (tue, 17, 19)]),
            ("Mathématiques", "3AS", "B", "Boudiaf", "Salle 1", 4500, 12, [(sat, 17, 19), (mon, 9, 11)]),
            ("Mathématiques", "4AM", "A", "Boudiaf", "Salle 2", 3500, 14, [(sat, 14, 16), (wed, 17, 19)]),
            ("Physique", "3AS", "A", "Hadjadj", "Labo", 4500, 8, [(sat, 11, 13), (mon, 17, 19)]),
            ("Physique", "2AS", "A", "Hadjadj", "Labo", 4000, 10, [(sun, 15, 17), (thu, 9, 11)]),
            ("Français", "4AM", "A", "Merabet", "Salle 3", 3000, 14, [(sat, 16, 18), (tue, 15, 17)]),
            ("Anglais", "3AS", "A", "Belhadj", "Salle 3", 3000, 14, [(sat, 9, 11), (wed, 15, 17)]),
            ("Sciences naturelles", "3AS", "A", "Saadi", "Salle 2", 4000, 12, [(sat, 10, 12), (thu, 14, 16)]),
            ("Philosophie", "3AS", "A", "Zitouni", "Salle 4", 3000, 16, [(sun, 9, 11), (thu, 16, 18)]),
            ("Arabe", "4AM", "A", "Kerroum", "Salle 4", 3000, 14, [(sat, 13, 15), (mon, 16, 18)]),
            ("Informatique", "2AS", "A", "Aït Ahmed", "Salle Info", 3500, 6, [(sun, 10, 12), (tue, 14, 16)]),
        };
        var groups = groupDefs.Select(g => new Group
        {
            Subject = S(g.Subj), Level = g.Level, MonthlyPrice = g.Price, Name = g.Name, Teacher = Tc(g.Teacher), Room = Rm(g.Room), Capacity = g.Cap,
            Slots = g.Slots.Select(s => new ScheduleSlot { Day = s.Item1, Start = TimeSpan.FromHours(s.Item2), End = TimeSpan.FromHours(s.Item3) }).ToList(),
        }).ToList();
        db.Groups.AddRange(groups);

        var sibling = new Discount { Name = "Fratrie", Type = DiscountType.Percent, Value = 10 };
        var quarterly = new Discount { Name = "Paiement trimestriel", Type = DiscountType.Percent, Value = 5 };
        var social = new Discount { Name = "Cas social", Type = DiscountType.FixedAmount, Value = 1500 };
        db.Discounts.AddRange(sibling, quarterly, social);

        string[] fn = ["Yacine", "Amira", "Rayan", "Lina", "Adam", "Sarah", "Anis", "Meriem", "Ilyes", "Nour", "Walid", "Inès", "Karim", "Yasmine", "Mehdi", "Aya", "Samy", "Rania", "Nassim", "Dounia", "Islam", "Imane", "Riad", "Hiba", "Amine"];
        string[] ln = ["Benali", "Haddad", "Bouzid", "Mansouri", "Cherif", "Belkacem", "Saidi", "Kaci", "Meziane", "Rahmani", "Bensaid", "Ferhat", "Toumi", "Amrani", "Hamidi", "Zerrouki", "Brahimi", "Djebbar", "Lounis", "Ouali"];
        string[] schools = ["Lycée Émir Abdelkader", "Lycée Ibn Khaldoun", "CEM Ali Boumendjel", "CEM El Feth", "Lycée Descartes"];
        string[] levels = ["3AS", "3AS", "3AS", "4AM", "4AM", "2AS"];
        var parents = new Dictionary<string, Parent>();
        var students = new List<Student>();
        for (var i = 0; i < 50; i++)
        {
            var last = Pick(ln);
            var level = Pick(levels);
            if (!parents.TryGetValue(last, out var parent))
            {
                parent = new Parent
                {
                    FullName = (R() < 0.5 ? "M. " : "Mme ") + last, Relation = R() < 0.5 ? "Père" : "Mère",
                    Phone = $"0{Pick(new[] { "5", "6", "7" })}{50 + (int)(R() * 49)} {10 + (int)(R() * 89)} {10 + (int)(R() * 89)} {10 + (int)(R() * 89)}",
                    Address = "Bab Ezzouar, Alger",
                };
                parents[last] = parent;
            }
            var isNew = i >= 43;
            var st = new Student
            {
                Matricule = "E" + (1001 + i), FirstName = fn[i * 7 % fn.Length], LastName = last, Level = level, Parent = parent,
                Gender = i % 2 == 0 ? Gender.Male : Gender.Female, IsActive = i % 13 != 5,
                School = level == "4AM" ? schools[2 + (int)(R() * 2)] : Pick(new[] { schools[0], schools[1], schools[4] }),
                BirthDate = new DateTime(level == "4AM" ? 2011 : level == "2AS" ? 2010 : 2008, 1 + (int)(R() * 12), 1 + (int)(R() * 27)),
                EnrolledOn = isNew ? period.AddDays(i % 20) : period.AddMonths(-(1 + (int)(R() * 20))).AddDays((int)(R() * 27)),
                Discount = R() < 0.15 ? sibling : null,
            };
            students.Add(st);
            if (!st.IsActive) continue;
            var available = groups.Where(g => g.Level == level).ToList();
            foreach (var g in available)
                if (R() < 0.5 && g.Enrollments.Count < g.Capacity)
                    g.Enrollments.Add(new Enrollment { Student = st, StartDate = st.EnrolledOn < period ? period.AddMonths(-1) : st.EnrolledOn });
            if (!groups.Any(g => g.Enrollments.Any(e => e.Student == st)))
                available.FirstOrDefault(g => g.Enrollments.Count < g.Capacity)?.Enrollments.Add(new Enrollment { Student = st, StartDate = st.EnrolledOn < period ? period.AddMonths(-1) : st.EnrolledOn });
        }
        db.Students.AddRange(students);
        await db.SaveChangesAsync(ct);

        // Payments for the current month.
        var settings = await db.Settings.FirstAsync(ct);
        foreach (var st in students.Where(s => s.IsActive))
        {
            st.Enrollments = groups.SelectMany(g => g.Enrollments).Where(e => e.StudentId == st.Id).ToList();
            var due = Billing.MonthlyDue(st, period);
            var x = R();
            var amount = x < 0.6 ? due : x < 0.82 ? Math.Round(due / 200) * 100 : 0;
            if (amount <= 0) continue;
            var day = Math.Min(today.Day, 1 + (int)(R() * Math.Max(1, today.Day)));
            db.StudentPayments.Add(new StudentPayment
            {
                ReceiptNumber = settings.ReceiptPrefix + settings.NextReceiptNumber++.ToString("0000"), StudentId = st.Id, Amount = amount,
                Kind = PaymentKind.Monthly, Period = period, Date = period.AddDays(day - 1).AddHours(9 + (int)(R() * 9)),
                Method = Pick(new[] { PaymentMethod.Cash, PaymentMethod.Cash, PaymentMethod.Cash, PaymentMethod.BaridiMob, PaymentMethod.Ccp }),
            });
        }

        // Teacher payments for the three previous months.
        foreach (var t in teachers.Where(t => t.IsActive))
            for (var m = 1; m <= 3; m++)
            {
                var p = period.AddMonths(-m);
                var earned = TeacherEarnings.Compute(t, groups.Where(g => g.TeacherId == t.Id), p, Money.Format).Amount;
                if (earned <= 0) continue;
                db.TeacherPayments.Add(new TeacherPayment { TeacherId = t.Id, Period = p, Date = Period.End(p).AddHours(16), Amount = earned, Method = PaymentMethod.Ccp });
            }

        foreach (var (cat, desc, amount) in new[] { ("Loyer", "Loyer du local", 60000m), ("Charges", "Électricité et eau", 9800m), ("Fournitures", "Marqueurs, papier, photocopies", 6400m), ("Internet", "Abonnement fibre", 3500m), ("Entretien", "Ménage et petites réparations", 8000m) })
            db.Expenses.Add(new Expense { Date = period.AddDays(Math.Min(4, today.Day - 1)), Category = cat, Description = desc, Amount = amount, Method = PaymentMethod.Cash });
        await db.SaveChangesAsync(ct);

        // Sessions of the current week with attendance taken for sessions already finished.
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 1) % 7)); // Saturday
        var now = clock.GetLocalNow().DateTime;
        foreach (var g in groups)
            foreach (var slot in g.Slots)
            {
                var date = weekStart.AddDays(((int)slot.Day + 1) % 7);
                var session = new Session { GroupId = g.Id, Date = date, Start = slot.Start, End = slot.End, RoomId = g.RoomId, TeacherId = g.TeacherId };
                if (date + slot.Start <= now)
                {
                    session.Status = SessionStatus.Done;
                    foreach (var e in g.Enrollments)
                    {
                        var r = R();
                        session.Attendance.Add(new AttendanceRecord { StudentId = e.StudentId, Status = r < 0.83 ? AttendanceStatus.Present : r < 0.92 ? AttendanceStatus.Late : AttendanceStatus.Absent });
                    }
                }
                db.Sessions.Add(session);
            }

        // Two evaluations per group with grades.
        foreach (var g in groups)
        {
            for (var k = 1; k <= 2; k++)
            {
                var exam = new Exam { GroupId = g.Id, Title = k == 1 ? "Test 1" : "Devoir 1", Type = k == 1 ? ExamType.Test : ExamType.Homework, Date = period.AddDays(Math.Min(today.Day - 1, 5 * k)) };
                foreach (var e in g.Enrollments)
                    exam.Grades.Add(new Grade { StudentId = e.StudentId, Score = Math.Round((decimal)(8 + R() * 11.5) * 2) / 2 });
                db.Exams.Add(exam);
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
