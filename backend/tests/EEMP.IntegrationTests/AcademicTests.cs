using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using EEMP.IntegrationTests.Infrastructure;
using static EEMP.IntegrationTests.Infrastructure.Api;

namespace EEMP.IntegrationTests;

/// <summary>Milestone 2 academic core: catalog, classes, enrollment, attendance, grades, report cards and portal visibility.</summary>
[Collection(ApiCollection.Name)]
public class AcademicTests(EempFactory factory)
{
    private readonly HttpClient _c = factory.CreateClient();

    private static string Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran")).DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Shift(string date, int days) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Weekday(string date) =>
        DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayOfWeek.ToString().ToLowerInvariant();

    private static string Rv(JsonNode n) => n["rowVersion"]!.GetValue<string>();

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..(prefix.Length + 8)].ToUpperInvariant();

    private sealed record Ctx(string Admin, string Manager, Guid North, Guid South, Guid TermId, Guid CourseId);

    /// <summary>A fresh academic year/term around today (so date rules hold whenever the suite runs) and a course.</summary>
    private async Task<Ctx> FreshAsync()
    {
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var manager = await Demo(_c, "branch.manager");
        var branches = (await Get(_c, "/api/v1/branches", admin))["items"]!.AsArray();
        Guid B(string code) => Id(branches.First(b => b!["code"]!.GetValue<string>() == code)!);
        var year = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/academic-years", admin,
            new { name = Unique("Y-"), startDate = Shift(Today, -60), endDate = Shift(Today, 300), status = "active" }), HttpStatusCode.Created);
        var term = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/terms", admin,
            new { academicYearId = Id(year), name = "T1", startDate = Shift(Today, -30), endDate = Shift(Today, 120) }), HttpStatusCode.Created);
        var subject = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/subjects", admin, new { code = Unique("SB"), name = "Chemistry" }), HttpStatusCode.Created);
        var course = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/courses", admin,
            new { subjectId = Id(subject), code = Unique("CO"), name = "Chemistry 8", gradeLevel = "8" }), HttpStatusCode.Created);
        return new Ctx(admin, manager.AccessToken, B("NORTH"), B("SOUTH"), Id(term), Id(course));
    }

    private async Task<JsonNode> ClassAsync(Ctx x, Guid branch, string token, int capacity = 30, string? room = null) =>
        await Ok(await Send(_c, HttpMethod.Post, "/api/v1/classes", token,
            new { termId = x.TermId, courseId = x.CourseId, branchId = branch, code = Unique("K"), name = "Class", capacity, status = "active", defaultRoom = room }),
            HttpStatusCode.Created);

    private async Task<Guid> PersonIdAsync(string token, string kind, string number)
    {
        var items = (await Get(_c, $"/api/v1/{kind}?search={number}&pageSize=100", token))["items"]!.AsArray();
        return Id(items.First(i => (i!["studentNumber"] ?? i["employeeNumber"])!.GetValue<string>() == number)!);
    }

    private async Task<Guid> NewStudentAsync(string managerToken, Guid branch) =>
        Id(await Ok(await Send(_c, HttpMethod.Post, "/api/v1/students", managerToken, new { branchId = branch, firstName = "Test", lastName = Unique("St") }),
            HttpStatusCode.Created));

    private async Task<JsonNode> EnrollAsync(string token, Guid classId, Guid studentId) =>
        await Ok(await Send(_c, HttpMethod.Post, "/api/v1/enrollments", token, new { classId, studentId }), HttpStatusCode.Created);

    private async Task<JsonNode> SeededClassAsync(string token, string code)
    {
        var items = (await Get(_c, $"/api/v1/classes?search={code}", token))["items"]!.AsArray();
        return items.First(i => i!["code"]!.GetValue<string>() == code)!;
    }

    [Fact]
    public async Task Catalog_crud_rules_and_permissions()
    {
        var x = await FreshAsync();
        // Seed + built-in schemes are visible.
        var schemes = (await Get(_c, "/api/v1/grading-schemes", x.Admin)).AsArray();
        var ir20 = schemes.Single(s => s!["code"]!.GetValue<string>() == "IR20")!;
        Assert.True(ir20["isDefault"]!.GetValue<bool>());
        Assert.Equal(20m, ir20["maxScore"]!.GetValue<decimal>());
        Assert.Contains(schemes, s => s!["code"]!.GetValue<string>() == "LETTER");
        Assert.Contains((await Get(_c, "/api/v1/academic-years?search=1405", x.Admin))["items"]!.AsArray(), y => y!["name"]!.GetValue<string>() == "1405-1406");

        var term = await Get(_c, $"/api/v1/terms/{x.TermId}", x.Admin);
        var yearId = term["academicYearId"]!.GetValue<string>();
        // Overlap and outside-year rules.
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/terms", x.Admin,
            new { academicYearId = yearId, name = "T2", startDate = Shift(Today, 100), endDate = Shift(Today, 150) }), HttpStatusCode.Conflict, "academic.term_overlap");
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/terms", x.Admin,
            new { academicYearId = yearId, name = "T3", startDate = Shift(Today, 200), endDate = Shift(Today, 400) }), HttpStatusCode.UnprocessableEntity, "academic.term_outside_year");
        var bad = await Send(_c, HttpMethod.Post, "/api/v1/academic-years", x.Admin, new { name = "X", startDate = "2027-01-01", endDate = "2026-01-01" });
        await Problem(bad, HttpStatusCode.BadRequest, "validation.failed");
        Assert.Contains("academic.invalid_date_range", (await bad.Content.ReadAsStringAsync()));
        await Problem(await Send(_c, HttpMethod.Delete, $"/api/v1/academic-years/{yearId}", x.Admin), HttpStatusCode.Conflict, "academic.year_in_use");

        // Branch manager can read the catalog but not change it (catalog.manage is tenant-wide).
        await Get(_c, "/api/v1/subjects", x.Manager);
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/subjects", x.Manager, new { code = "NOPE", name = "No" }), HttpStatusCode.Forbidden, "authorization.forbidden");
        // Students/guardians have no catalog permission.
        var guardian = (await Demo(_c, "guardian.one")).AccessToken;
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/subjects", guardian), HttpStatusCode.Forbidden, "authorization.forbidden");

        // Term rowversion conflict.
        var update = new { name = "T1 renamed", startDate = term["startDate"]!.GetValue<string>(), endDate = term["endDate"]!.GetValue<string>(), rowVersion = Rv(term) };
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/terms/{x.TermId}", x.Admin, update));
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/terms/{x.TermId}", x.Admin, update), HttpStatusCode.Conflict, "concurrency.conflict");
    }

    [Fact]
    public async Task Classes_schedule_conflicts_teachers_and_branch_scope()
    {
        var x = await FreshAsync();
        var mina = await PersonIdAsync(x.Manager, "teachers", "T0001");
        var a = await ClassAsync(x, x.North, x.Manager, room: "R1");
        Assert.Equal("IR20", (await Get(_c, "/api/v1/grading-schemes", x.Admin)).AsArray().Single(s => Id(s!) == Guid.Parse(a["gradingSchemeId"]!.GetValue<string>()))!["code"]!.GetValue<string>());

        // Slot validation: start >= end and overlapping slots of the same class.
        var invalid = await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(a)}/schedule", x.Manager, new
        {
            rowVersion = Rv(a),
            slots = new[] { new { weekday = "saturday", startTime = "10:00", endTime = "09:00", room = (string?)null } },
        });
        await Problem(invalid, HttpStatusCode.BadRequest, "validation.failed");

        a = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(a)}/schedule", x.Manager, new
        {
            rowVersion = Rv(a),
            slots = new[] { new { weekday = "saturday", startTime = "08:00", endTime = "09:30", room = (string?)null } },
        }));
        Assert.Equal("R1", a["schedule"]![0]!["room"]!.GetValue<string>());
        a = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(a)}/teachers", x.Manager,
            new { rowVersion = Rv(a), assignments = new[] { new { teacherId = mina, role = "primary" } } }));
        Assert.True(a["teachers"]![0]!["isActive"]!.GetValue<bool>());
        Assert.Equal("Mina", a["teachers"]![0]!["firstName"]!.GetValue<string>());

        // Same room, overlapping → room conflict; same teacher overlapping → teacher conflict.
        var b = await ClassAsync(x, x.North, x.Manager, room: "R1");
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(b)}/schedule", x.Manager, new
        {
            rowVersion = Rv(b), slots = new[] { new { weekday = "saturday", startTime = "09:00", endTime = "10:00", room = (string?)null } },
        }), HttpStatusCode.Conflict, "academic.room_conflict");
        b = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(b)}/schedule", x.Manager, new
        {
            rowVersion = Rv(b), slots = new[] { new { weekday = "saturday", startTime = "09:00", endTime = "10:00", room = (string?)"R2" } },
        }));
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(b)}/teachers", x.Manager,
            new { rowVersion = Rv(b), assignments = new[] { new { teacherId = mina, role = "primary" } } }), HttpStatusCode.Conflict, "academic.teacher_conflict");

        // A south teacher cannot be assigned to a north class.
        var hamid = await PersonIdAsync(await SouthReaderAsync(), "teachers", "T0002");
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(b)}/teachers", x.Manager,
            new { rowVersion = Rv(b), assignments = new[] { new { teacherId = hamid, role = "primary" } } }), HttpStatusCode.UnprocessableEntity, "academic.teacher_branch_mismatch");

        // Branch manager (North) cannot create in South and does not see South classes.
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/classes", x.Manager,
            new { termId = x.TermId, courseId = x.CourseId, branchId = x.South, code = Unique("K"), name = "S", capacity = 5 }), HttpStatusCode.Forbidden, "authorization.forbidden_scope");
        var phys = await SeededClassAsync(x.Admin, "PHYS-7S");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/classes/{Id(phys)}", x.Manager), HttpStatusCode.NotFound, "resource.not_found");
        Assert.All((await Get(_c, "/api/v1/classes?pageSize=100", x.Manager))["items"]!.AsArray(), c => Assert.Equal(x.North, Guid.Parse(c!["branchId"]!.GetValue<string>())));

        // Stale class rowversion → 409; duplicate code in term → 409 resource.duplicate.
        var upd = new { code = a["code"]!.GetValue<string>(), name = "Renamed", capacity = 20, rowVersion = "AAAAAAAAAAA=" };
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(a)}", x.Manager, upd), HttpStatusCode.Conflict, "concurrency.conflict");
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/classes", x.Manager,
            new { termId = x.TermId, courseId = x.CourseId, branchId = x.North, code = a["code"]!.GetValue<string>(), name = "Dup", capacity = 5 }),
            HttpStatusCode.Conflict, "resource.duplicate");
    }

    private async Task<string> SouthReaderAsync()
    {
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var branches = (await Get(_c, "/api/v1/branches", admin))["items"]!.AsArray();
        var south = Id(branches.First(b => b!["code"]!.GetValue<string>() == "SOUTH")!);
        var roles = (await Get(_c, "/api/v1/roles", admin)).AsArray();
        var role = roles.FirstOrDefault(r => r!["code"]!.GetValue<string>() == "SouthTeacherReader")
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/roles", admin, new { code = "SouthTeacherReader", name = "x", permissions = new[] { "teachers.read" } }), HttpStatusCode.Created);
        var users = (await Get(_c, "/api/v1/users?search=south.treader", admin))["items"]!.AsArray();
        var user = users.FirstOrDefault()
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", admin, new { userName = "south.treader", displayName = "x", password = "Reader123" }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(user)}/assignments", admin, new[] { new { roleId = Id(role), branchId = (Guid?)south } }));
        return (await Login(_c, "demo", "south.treader", "Reader123")).AccessToken;
    }

    [Fact]
    public async Task Enrollment_capacity_branch_and_timetable_rules()
    {
        var x = await FreshAsync();
        var small = await ClassAsync(x, x.North, x.Manager, capacity: 1);
        var s1 = await NewStudentAsync(x.Manager, x.North);
        var s2 = await NewStudentAsync(x.Manager, x.North);
        var e1 = await EnrollAsync(x.Manager, Id(small), s1);
        Assert.Equal("active", e1["status"]!.GetValue<string>());
        Assert.Equal(Today, e1["enrolledOn"]!.GetValue<string>());
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/enrollments", x.Manager, new { classId = Id(small), studentId = s1 }), HttpStatusCode.Conflict, "academic.already_enrolled");
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/enrollments", x.Manager, new { classId = Id(small), studentId = s2 }), HttpStatusCode.Conflict, "academic.class_full");
        Assert.Equal(1, (await Get(_c, $"/api/v1/classes/{Id(small)}", x.Manager))["enrolledCount"]!.GetValue<int>());

        // Withdraw frees the seat; stale rowversion on withdraw → 409.
        var w = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/enrollments/{Id(e1)}/withdraw", x.Manager, new { rowVersion = Rv(e1) }));
        Assert.Equal("withdrawn", w["status"]!.GetValue<string>());
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/enrollments/{Id(e1)}/withdraw", x.Manager, new { rowVersion = Rv(e1) }), HttpStatusCode.Conflict, "academic.invalid_transition");
        await EnrollAsync(x.Manager, Id(small), s2);

        // Branch mismatch: a south student (seed S0003) cannot join a north class.
        var maryam = await PersonIdAsync(await SouthStudentReaderAsync(), "students", "S0003");
        var open = await ClassAsync(x, x.North, x.Manager);
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/enrollments", x.Manager, new { classId = Id(open), studentId = maryam }),
            HttpStatusCode.UnprocessableEntity, "academic.student_branch_mismatch");

        // Timetable conflict within the same term.
        var p = await ClassAsync(x, x.North, x.Manager);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(p)}/schedule", x.Manager,
            new { rowVersion = Rv(p), slots = new[] { new { weekday = "tuesday", startTime = "13:00", endTime = "14:00", room = "P1" } } }));
        var q = await ClassAsync(x, x.North, x.Manager);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(q)}/schedule", x.Manager,
            new { rowVersion = Rv(q), slots = new[] { new { weekday = "tuesday", startTime = "13:30", endTime = "14:30", room = "P2" } } }));
        await EnrollAsync(x.Manager, Id(p), s1);
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/enrollments", x.Manager, new { classId = Id(q), studentId = s1 }), HttpStatusCode.Conflict, "academic.schedule_conflict");

        // Enrolled student cannot be deleted (people.in_use); roster lists active students only.
        await Problem(await Send(_c, HttpMethod.Delete, $"/api/v1/students/{s1}", x.Manager), HttpStatusCode.Conflict, "people.in_use");
        var roster = (await Get(_c, $"/api/v1/classes/{Id(small)}/roster", x.Manager)).AsArray();
        Assert.Equal([s2], roster.Select(r => Guid.Parse(r!["studentId"]!.GetValue<string>())).ToArray());
        Assert.Equal(2, (await Get(_c, $"/api/v1/classes/{Id(small)}/roster?includeInactive=true", x.Manager)).AsArray().Count);

        // Tenant admin sees classes (catalog/classes perms) but not rosters or enrollments (sensitive).
        await Get(_c, $"/api/v1/classes/{Id(small)}", x.Admin);
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/classes/{Id(small)}/roster", x.Admin), HttpStatusCode.Forbidden, "authorization.forbidden_scope");
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/enrollments", x.Admin), HttpStatusCode.Forbidden, "authorization.forbidden");
    }

    private async Task<string> SouthStudentReaderAsync()
    {
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var branches = (await Get(_c, "/api/v1/branches", admin))["items"]!.AsArray();
        var south = Id(branches.First(b => b!["code"]!.GetValue<string>() == "SOUTH")!);
        var roles = (await Get(_c, "/api/v1/roles", admin)).AsArray();
        var role = roles.FirstOrDefault(r => r!["code"]!.GetValue<string>() == "SouthStudentReader")
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/roles", admin, new { code = "SouthStudentReader", name = "x", permissions = new[] { "students.read" } }), HttpStatusCode.Created);
        var users = (await Get(_c, "/api/v1/users?search=south.sreader", admin))["items"]!.AsArray();
        var user = users.FirstOrDefault()
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", admin, new { userName = "south.sreader", displayName = "x", password = "Reader123" }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(user)}/assignments", admin, new[] { new { roleId = Id(role), branchId = (Guid?)south } }));
        return (await Login(_c, "demo", "south.sreader", "Reader123")).AccessToken;
    }

    [Fact]
    public async Task Bulk_attendance_is_idempotent_and_validated()
    {
        var x = await FreshAsync();
        var cls = await ClassAsync(x, x.North, x.Manager);
        cls = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(cls)}/schedule", x.Manager,
            new { rowVersion = Rv(cls), slots = new[] { new { weekday = Weekday(Today), startTime = "07:00", endTime = "07:45", room = Unique("A") } } }));
        var slotId = Guid.Parse(cls["schedule"]![0]!["id"]!.GetValue<string>());
        var s1 = await NewStudentAsync(x.Manager, x.North);
        var s2 = await NewStudentAsync(x.Manager, x.North);
        await EnrollAsync(x.Manager, Id(cls), s1);
        await EnrollAsync(x.Manager, Id(cls), s2);

        var body = new { date = Today, scheduleSlotId = slotId, records = new[] { new { studentId = s1, status = "present", note = (string?)null }, new { studentId = s2, status = "late", note = (string?)"bus" } } };
        var first = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(cls)}/attendance", x.Manager, body));
        var second = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(cls)}/attendance", x.Manager, body));
        Assert.Equal(first["sessionId"]!.GetValue<string>(), second["sessionId"]!.GetValue<string>());
        Assert.Equal(first["rowVersion"]!.GetValue<string>(), second["rowVersion"]!.GetValue<string>()); // no-op replay does not rewrite
        Assert.Equal(1, second["summary"]!["present"]!.GetValue<int>());
        Assert.Equal(1, second["summary"]!["late"]!.GetValue<int>());
        Assert.Equal(0, second["summary"]!["unmarked"]!.GetValue<int>());
        var sessions = (await Get(_c, $"/api/v1/classes/{Id(cls)}/attendance/sessions", x.Manager)).AsArray();
        Assert.Single(sessions);

        // Partial update changes only the listed student; a stale session rowversion is rejected.
        var third = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(cls)}/attendance", x.Manager,
            new { date = Today, scheduleSlotId = slotId, rowVersion = Rv(second), records = new[] { new { studentId = s1, status = "absent" } } }));
        Assert.Equal(1, third["summary"]!["absent"]!.GetValue<int>());
        Assert.Equal(1, third["summary"]!["late"]!.GetValue<int>());
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(cls)}/attendance", x.Manager,
            new { date = Today, scheduleSlotId = slotId, rowVersion = Rv(second), records = new[] { new { studentId = s1, status = "excused" } } }),
            HttpStatusCode.Conflict, "concurrency.conflict");

        // Rules: future date, wrong weekday for slot, not enrolled, outside term.
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(cls)}/attendance", x.Manager,
            new { date = Shift(Today, 1), records = new[] { new { studentId = s1, status = "present" } } }), HttpStatusCode.UnprocessableEntity, "academic.attendance_future_date");
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(cls)}/attendance", x.Manager,
            new { date = Shift(Today, -1), scheduleSlotId = slotId, records = new[] { new { studentId = s1, status = "present" } } }), HttpStatusCode.UnprocessableEntity, "academic.slot_weekday_mismatch");
        var stranger = await NewStudentAsync(x.Manager, x.North);
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(cls)}/attendance", x.Manager,
            new { date = Today, records = new[] { new { studentId = stranger, status = "present" } } }), HttpStatusCode.UnprocessableEntity, "academic.student_not_enrolled");
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(cls)}/attendance", x.Manager,
            new { date = Shift(Today, -45), records = new[] { new { studentId = s1, status = "present" } } }), HttpStatusCode.UnprocessableEntity, "academic.date_outside_term");

        // Staff record listing is branch-scoped.
        var list = await Get(_c, $"/api/v1/attendance?classId={Id(cls)}", x.Manager);
        Assert.Equal(2, list["totalCount"]!.GetValue<int>());

        // Concurrent first writes of the same sheet converge on one session.
        var day = Shift(Today, -2);
        var tasks = Enumerable.Range(0, 4).Select(_ => Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(cls)}/attendance", x.Manager,
            new { date = day, records = new[] { new { studentId = s1, status = "present" } } })).ToList();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, r.StatusCode.ToString()));
        Assert.Equal(2, (await Get(_c, $"/api/v1/classes/{Id(cls)}/attendance/sessions", x.Manager)).AsArray().Count);
    }

    [Fact]
    public async Task Teacher_acts_only_on_assigned_classes()
    {
        var x = await FreshAsync();
        var teacher = (await Demo(_c, "teacher.one")).AccessToken;
        var mina = await PersonIdAsync(x.Manager, "teachers", "T0001");
        var mine = await ClassAsync(x, x.North, x.Manager);
        mine = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(mine)}/teachers", x.Manager,
            new { rowVersion = Rv(mine), assignments = new[] { new { teacherId = mina, role = "primary" } } }));
        var other = await ClassAsync(x, x.North, x.Manager);
        var s = await NewStudentAsync(x.Manager, x.North);
        await EnrollAsync(x.Manager, Id(mine), s);
        await EnrollAsync(x.Manager, Id(other), s);

        // My classes lists assigned classes only (seeded MATH-7A + this one).
        var myClasses = (await Get(_c, "/api/v1/portal/me/classes", teacher)).AsArray();
        Assert.Contains(myClasses, c => Id(c!) == Id(mine));
        Assert.Contains(myClasses, c => c!["code"]!.GetValue<string>() == "MATH-7A");
        Assert.DoesNotContain(myClasses, c => Id(c!) == Id(other));

        // Assigned class: read, roster, attendance, assessments, scores, submit.
        await Get(_c, $"/api/v1/classes/{Id(mine)}", teacher);
        Assert.Single((await Get(_c, $"/api/v1/classes/{Id(mine)}/roster", teacher)).AsArray());
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(mine)}/attendance", teacher,
            new { date = Today, records = new[] { new { studentId = s, status = "present" } } }));
        var a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/classes/{Id(mine)}/assessments", teacher,
            new { name = "Lab", type = "project", weight = 1, maxScore = 20 }), HttpStatusCode.Created);
        var res = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/assessments/{Id(a)}/results", teacher,
            new { rowVersion = Rv(a), results = new[] { new { studentId = s, mark = "scored", score = 19.25m } } }));
        a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/submit", teacher, new { rowVersion = Rv(res["assessment"]!) }));
        Assert.Equal("submitted", a["status"]!.GetValue<string>());
        // ...but not approve/publish, and not change the class.
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/approve", teacher, new { rowVersion = Rv(a) }), HttpStatusCode.Forbidden, "authorization.forbidden");
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(mine)}/schedule", teacher, new { rowVersion = Rv(mine), slots = Array.Empty<object>() }),
            HttpStatusCode.Forbidden, "authorization.forbidden");

        // Unassigned class (same branch): invisible.
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/classes/{Id(other)}", teacher), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/classes/{Id(other)}/roster", teacher), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(other)}/attendance", teacher,
            new { date = Today, records = new[] { new { studentId = s, status = "present" } } }), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/classes/{Id(other)}/assessments", teacher,
            new { name = "X", type = "quiz", weight = 1, maxScore = 10 }), HttpStatusCode.NotFound, "resource.not_found");
        var phys = await SeededClassAsync(x.Admin, "PHYS-7S");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/classes/{Id(phys)}/gradebook", teacher), HttpStatusCode.NotFound, "resource.not_found");
        // Teacher has no staff permissions: staff lists are forbidden.
        await Problem(await Send(_c, HttpMethod.Get, "/api/v1/enrollments", teacher), HttpStatusCode.Forbidden, "authorization.forbidden");

        // An ended assignment removes access.
        mine = await Get(_c, $"/api/v1/classes/{Id(mine)}", x.Manager);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/classes/{Id(mine)}/teachers", x.Manager, new
        {
            rowVersion = Rv(mine), assignments = new[] { new { teacherId = mina, role = "primary", startDate = Shift(Today, -20), endDate = Shift(Today, -1) } },
        }));
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/classes/{Id(mine)}", teacher), HttpStatusCode.NotFound, "resource.not_found");
    }

    [Fact]
    public async Task Grade_workflow_publish_gating_history_and_portal_visibility()
    {
        var manager = (await Demo(_c, "branch.manager")).AccessToken;
        var teacher = (await Demo(_c, "teacher.one")).AccessToken;
        var guardian = (await Demo(_c, "guardian.one")).AccessToken;
        var math = await SeededClassAsync(manager, "MATH-7A");
        var sara = await PersonIdAsync(manager, "students", "S0001");
        var ali = await PersonIdAsync(manager, "students", "S0002");

        // Seeded state: Quiz 1 published, Midterm draft → portal shows only Quiz 1.
        var grades = (await Get(_c, $"/api/v1/portal/me/students/{sara}/grades?termId={math["termId"]!.GetValue<string>()}", guardian)).AsArray();
        var mathGrades = grades.Single(g => Guid.Parse(g!["classId"]!.GetValue<string>()) == Id(math))!;
        var names = mathGrades["assessments"]!.AsArray().Select(a => a!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("Quiz 1", names);
        Assert.DoesNotContain("Midterm", names);
        Assert.Equal(18.5m, mathGrades["assessments"]!.AsArray().First(a => a!["name"]!.GetValue<string>() == "Quiz 1")!["score"]!.GetValue<decimal>());

        // Teacher creates an assessment, enters scores, submits; manager returns, teacher fixes, manager approves and publishes.
        var a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/classes/{Id(math)}/assessments", teacher,
            new { name = Unique("HW-"), type = "assignment", weight = 1, maxScore = 10 }), HttpStatusCode.Created);
        var name = a["name"]!.GetValue<string>();
        var incomplete = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/assessments/{Id(a)}/results", teacher,
            new { rowVersion = Rv(a), results = new[] { new { studentId = sara, mark = "scored", score = (decimal?)8m } } }));
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/submit", teacher, new { rowVersion = Rv(incomplete["assessment"]!) }),
            HttpStatusCode.UnprocessableEntity, "academic.results_incomplete");
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/assessments/{Id(a)}/results", teacher,
            new { rowVersion = Rv(incomplete["assessment"]!), results = new[] { new { studentId = ali, mark = "scored", score = (decimal?)11m } } }),
            HttpStatusCode.BadRequest, "validation.failed"); // above maxScore
        var full = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/assessments/{Id(a)}/results", teacher,
            new { rowVersion = Rv(incomplete["assessment"]!), results = new[] { new { studentId = ali, mark = "absent", score = (decimal?)null } } }));
        // Stale score-sheet rowversion → 409 (concurrent editors).
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/assessments/{Id(a)}/results", teacher,
            new { rowVersion = Rv(incomplete["assessment"]!), results = new[] { new { studentId = ali, mark = "exempt", score = (decimal?)null } } }),
            HttpStatusCode.Conflict, "concurrency.conflict");
        a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/submit", teacher, new { rowVersion = Rv(full["assessment"]!) }));
        await Problem(await Send(_c, HttpMethod.Put, $"/api/v1/assessments/{Id(a)}/results", teacher,
            new { rowVersion = Rv(a), results = new[] { new { studentId = sara, mark = "scored", score = (decimal?)9m } } }), HttpStatusCode.Conflict, "academic.assessment_locked");
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/return", manager, new { rowVersion = Rv(a) }), HttpStatusCode.BadRequest, "validation.failed");
        a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/return", manager, new { rowVersion = Rv(a), reason = "Recheck Ali" }));
        Assert.Equal("draft", a["status"]!.GetValue<string>());
        Assert.Equal("Recheck Ali", a["returnReason"]!.GetValue<string>());
        var fixedRes = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/assessments/{Id(a)}/results", teacher,
            new { rowVersion = Rv(a), results = new[] { new { studentId = ali, mark = "scored", score = (decimal?)6m, reason = "late submission accepted" } } }));
        a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/submit", teacher, new { rowVersion = Rv(fixedRes["assessment"]!) }));
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/publish", manager, new { rowVersion = Rv(a) }), HttpStatusCode.Conflict, "academic.invalid_transition");
        a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/approve", manager, new { rowVersion = Rv(a) }));

        // Approved but unpublished: still invisible in the portal.
        async Task<JsonNode?> PortalAssessment(string token, Guid student) =>
            (await Get(_c, $"/api/v1/portal/me/students/{student}/grades?termId={math["termId"]!.GetValue<string>()}", token)).AsArray()
                .SelectMany(g => g!["assessments"]!.AsArray()).FirstOrDefault(x => x!["name"]!.GetValue<string>() == name);
        Assert.Null(await PortalAssessment(guardian, sara));

        a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/publish", manager, new { rowVersion = Rv(a) }));
        Assert.Equal("published", a["status"]!.GetValue<string>());
        Assert.Equal(8m, (await PortalAssessment(guardian, sara))!["score"]!.GetValue<decimal>());
        var student = (await Demo(_c, "student.one")).AccessToken;
        Assert.Equal(8m, (await PortalAssessment(student, sara))!["score"]!.GetValue<decimal>());

        // Correction: reopen (reason required) → portal keeps the published 8 until republished as 9.
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/reopen", teacher, new { rowVersion = Rv(a), reason = "x" }), HttpStatusCode.Forbidden, "authorization.forbidden");
        a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/reopen", manager, new { rowVersion = Rv(a), reason = "Typo" }));
        var corrected = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/assessments/{Id(a)}/results", teacher,
            new { rowVersion = Rv(a), results = new[] { new { studentId = sara, mark = "scored", score = (decimal?)9m, reason = "typo" } } }));
        Assert.Equal(8m, (await PortalAssessment(guardian, sara))!["score"]!.GetValue<decimal>());
        a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/submit", teacher, new { rowVersion = Rv(corrected["assessment"]!) }));
        a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/approve", manager, new { rowVersion = Rv(a) }));
        await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/publish", manager, new { rowVersion = Rv(a) }));
        Assert.Equal(9m, (await PortalAssessment(guardian, sara))!["score"]!.GetValue<decimal>());

        // Grade history: every score change with old/new values and every transition.
        var history = (await Get(_c, $"/api/v1/assessments/{Id(a)}/history", teacher)).AsArray();
        var saraChanges = history.Where(h => h!["action"]!.GetValue<string>() == "scoreChanged" && h["studentId"]?.GetValue<string>() == sara.ToString()).ToList();
        Assert.Equal(2, saraChanges.Count);
        Assert.Equal(8m, saraChanges[1]!["oldScore"]!.GetValue<decimal>());
        Assert.Equal(9m, saraChanges[1]!["newScore"]!.GetValue<decimal>());
        Assert.Equal(["submitted", "returned", "submitted", "approved", "published", "reopened", "submitted", "approved", "published"],
            history.Select(h => h!["action"]!.GetValue<string>()).Where(s => s != "scoreChanged").ToArray());

        // Gradebook (staff) uses live scores of all assessments incl. the draft midterm.
        var book = await Get(_c, $"/api/v1/classes/{Id(math)}/gradebook", manager);
        Assert.Equal("IR20", book["gradingScheme"]!["code"]!.GetValue<string>());
        Assert.NotNull(book["students"]!.AsArray().First(s => s!["studentId"]!.GetValue<string>() == sara.ToString())!["total"]);

        // Guardian cannot see an unlinked student (S0002) or staff endpoints.
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/students/{ali}/grades", guardian), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/students/{ali}/timetable", student), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/assessments/{Id(a)}/results", guardian), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/classes/{Id(math)}/gradebook", student), HttpStatusCode.NotFound, "resource.not_found");
    }

    [Fact]
    public async Task Portal_timetable_and_attendance_for_linked_student()
    {
        var guardian = (await Demo(_c, "guardian.one")).AccessToken;
        var manager = (await Demo(_c, "branch.manager")).AccessToken;
        var sara = await PersonIdAsync(manager, "students", "S0001");
        var math = await SeededClassAsync(manager, "MATH-7A");
        var termId = math["termId"]!.GetValue<string>();
        var tt = (await Get(_c, $"/api/v1/portal/me/students/{sara}/timetable?termId={termId}", guardian)).AsArray();
        var mathSlots = tt.Where(e => e!["classCode"]!.GetValue<string>() == "MATH-7A").ToList();
        Assert.Equal(["saturday", "monday"], mathSlots.Select(e => e!["weekday"]!.GetValue<string>()).ToArray());
        Assert.Equal("08:00", mathSlots[0]!["startTime"]!.GetValue<string>());
        Assert.Equal("101", mathSlots[0]!["room"]!.GetValue<string>());
        Assert.Equal("Rahimi", mathSlots[0]!["teachers"]![0]!["lastName"]!.GetValue<string>());

        var att = await Get(_c, $"/api/v1/portal/me/students/{sara}/attendance?from=2026-11-21&to=2026-11-21", guardian);
        Assert.Equal("present", att["records"]!.AsArray().Single()!["status"]!.GetValue<string>());
        Assert.Equal(1, att["summary"]!["present"]!.GetValue<int>());

        // Admin (no profile) has no linked students.
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/students/{sara}/timetable", admin), HttpStatusCode.NotFound, "resource.not_found");
        Assert.Empty((await Get(_c, "/api/v1/portal/me/classes", admin)).AsArray());
    }

    [Fact]
    public async Task Report_cards_are_versioned_and_portal_sees_published_only()
    {
        var x = await FreshAsync();
        var guardianLogin = await Demo(_c, "guardian.one");
        var guardian = guardianLogin.AccessToken;
        var sara = await PersonIdAsync(x.Manager, "students", "S0001");
        var cls = await ClassAsync(x, x.North, x.Manager);
        await EnrollAsync(x.Manager, Id(cls), sara);
        var a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/classes/{Id(cls)}/assessments", x.Manager,
            new { name = "Final", type = "final", weight = 3, maxScore = 20 }), HttpStatusCode.Created);
        var r = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/assessments/{Id(a)}/results", x.Manager,
            new { rowVersion = Rv(a), results = new[] { new { studentId = sara, mark = "scored", score = 16m } } }));
        a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/submit", x.Manager, new { rowVersion = Rv(r["assessment"]!) }));
        a = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(a)}/approve", x.Manager, new { rowVersion = Rv(a) }));

        // Tenant admin may not generate (sensitive); manager can.
        await Problem(await Send(_c, HttpMethod.Post, "/api/v1/report-cards/generate", x.Admin, new { termId = x.TermId }), HttpStatusCode.Forbidden, "authorization.forbidden");
        var gen = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/report-cards/generate", x.Manager, new { termId = x.TermId, classId = Id(cls) }));
        Assert.Equal(1, gen["created"]!.GetValue<int>());
        var card = gen["items"]![0]!;
        Assert.Equal("draft", card["status"]!.GetValue<string>());
        Assert.Equal(16m, card["average"]!["value"]!.GetValue<decimal>());
        var detail = await Get(_c, $"/api/v1/report-cards/{Id(card)}", x.Manager);
        Assert.Equal("weightedAverage.v1", detail["content"]!["formula"]!.GetValue<string>());
        Assert.Equal("good", detail["content"]!["lines"]![0]!["total"]!["label"]!.GetValue<string>());

        // Regenerate with no changes → unchanged; draft is not visible in the portal.
        Assert.Equal(1, (await Ok(await Send(_c, HttpMethod.Post, "/api/v1/report-cards/generate", x.Manager, new { termId = x.TermId, classId = Id(cls) })))["unchanged"]!.GetValue<int>());
        Assert.DoesNotContain((await Get(_c, $"/api/v1/portal/me/students/{sara}/report-cards", guardian)).AsArray(), c => Id(c!) == Id(card));
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/portal/me/report-cards/{Id(card)}", guardian), HttpStatusCode.NotFound, "resource.not_found");

        // Publish v1 (stale rowversion → 409), then the guardian sees it.
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/report-cards/{Id(card)}/publish", x.Manager, new { rowVersion = "AAAAAAAAAAA=" }),
            HttpStatusCode.Conflict, "concurrency.conflict");
        var published = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/report-cards/{Id(card)}/publish", x.Manager, new { rowVersion = Rv(detail) }));
        Assert.Equal(1, published["publishedVersion"]!.GetValue<int>());
        await Problem(await Send(_c, HttpMethod.Post, $"/api/v1/report-cards/{Id(card)}/publish", x.Manager, new { rowVersion = Rv(published) }),
            HttpStatusCode.Conflict, "academic.invalid_transition");
        var portalCard = await Get(_c, $"/api/v1/portal/me/report-cards/{Id(card)}", guardian);
        Assert.Equal(1, portalCard["versionNo"]!.GetValue<int>());
        Assert.Equal(16m, portalCard["content"]!["average"]!["value"]!.GetValue<decimal>());

        // Correction: a new approved assessment → regenerate creates v2 (draft); portal still shows v1 until v2 is published.
        var b = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/classes/{Id(cls)}/assessments", x.Manager,
            new { name = "Oral", type = "participation", weight = 1, maxScore = 20 }), HttpStatusCode.Created);
        var rb = await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/assessments/{Id(b)}/results", x.Manager,
            new { rowVersion = Rv(b), results = new[] { new { studentId = sara, mark = "scored", score = 20m } } }));
        b = await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(b)}/submit", x.Manager, new { rowVersion = Rv(rb["assessment"]!) }));
        await Ok(await Send(_c, HttpMethod.Post, $"/api/v1/assessments/{Id(b)}/approve", x.Manager, new { rowVersion = Rv(b) }));
        var regen = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/report-cards/generate", x.Manager, new { termId = x.TermId, branchId = x.North }));
        Assert.True(regen["regenerated"]!.GetValue<int>() >= 1);
        var v2 = await Get(_c, $"/api/v1/report-cards/{Id(card)}", x.Manager);
        Assert.Equal(2, v2["currentVersion"]!.GetValue<int>());
        Assert.Equal(17m, v2["average"]!["value"]!.GetValue<decimal>()); // (3*0.8 + 1*1.0)/4 = 0.85 → 17
        Assert.Equal(16m, (await Get(_c, $"/api/v1/portal/me/report-cards/{Id(card)}", guardian))["content"]!["average"]!["value"]!.GetValue<decimal>());

        var batch = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/report-cards/publish", x.Manager, new { termId = x.TermId, classId = Id(cls) }));
        Assert.Equal(1, batch["published"]!.GetValue<int>());
        var after = await Get(_c, $"/api/v1/report-cards/{Id(card)}", x.Manager);
        Assert.Equal(["superseded", "published"], after["versions"]!.AsArray().Select(v => v!["status"]!.GetValue<string>()).ToArray());
        var v1 = await Get(_c, $"/api/v1/report-cards/{Id(card)}/versions/1", x.Manager);
        Assert.Equal(16m, v1["content"]!["average"]!["value"]!.GetValue<decimal>()); // prior publication preserved
        Assert.Equal(17m, (await Get(_c, $"/api/v1/portal/me/report-cards/{Id(card)}", guardian))["content"]!["average"]!["value"]!.GetValue<decimal>());

        // Branch isolation: South-only reader with report-card permission cannot see the North card.
        var southReader = await RoleUserAsync("south.rc", "SouthRc", ["reportcards.read"], x.South);
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/report-cards/{Id(card)}", southReader), HttpStatusCode.NotFound, "resource.not_found");
        Assert.DoesNotContain((await Get(_c, $"/api/v1/report-cards?termId={x.TermId}", southReader))["items"]!.AsArray(), c => Id(c!) == Id(card));
    }

    private async Task<string> RoleUserAsync(string userName, string roleCode, string[] permissions, Guid branchId)
    {
        var admin = (await Demo(_c, "demo.admin")).AccessToken;
        var roles = (await Get(_c, "/api/v1/roles", admin)).AsArray();
        var role = roles.FirstOrDefault(r => r!["code"]!.GetValue<string>() == roleCode)
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/roles", admin, new { code = roleCode, name = roleCode, permissions }), HttpStatusCode.Created);
        var users = (await Get(_c, $"/api/v1/users?search={userName}", admin))["items"]!.AsArray();
        var user = users.FirstOrDefault()
            ?? await Ok(await Send(_c, HttpMethod.Post, "/api/v1/users", admin, new { userName, displayName = userName, password = "Reader123" }), HttpStatusCode.Created);
        await Ok(await Send(_c, HttpMethod.Put, $"/api/v1/users/{Id(user)}/assignments", admin, new[] { new { roleId = Id(role), branchId = (Guid?)branchId } }));
        return (await Login(_c, "demo", userName, "Reader123")).AccessToken;
    }

    [Fact]
    public async Task Academic_data_is_isolated_between_tenants()
    {
        var platform = await Login(_c, null, "platform.admin", EempFactory.PlatformPassword);
        var slug = $"a{Guid.NewGuid():N}"[..12];
        await Ok(await Send(_c, HttpMethod.Post, "/api/v1/platform/tenants", platform.AccessToken, new
        {
            slug, name = "Academic Other", adminUser = new { userName = "acad.admin", displayName = "Other", password = "OtherAdmin1" },
        }), HttpStatusCode.Created);
        var other = (await Login(_c, slug, "acad.admin", "OtherAdmin1")).AccessToken;
        var demoAdmin = (await Demo(_c, "demo.admin")).AccessToken;
        var math = await SeededClassAsync(demoAdmin, "MATH-7A");
        var termId = math["termId"]!.GetValue<string>();

        // The other tenant gets its own built-in schemes and sees none of demo's catalog or classes.
        var schemes = (await Get(_c, "/api/v1/grading-schemes", other)).AsArray();
        Assert.Equal(3, schemes.Count);
        Assert.Equal(0, (await Get(_c, "/api/v1/academic-years", other))["totalCount"]!.GetValue<int>());
        Assert.Equal(0, (await Get(_c, "/api/v1/classes", other))["totalCount"]!.GetValue<int>());
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/classes/{Id(math)}", other), HttpStatusCode.NotFound, "resource.not_found");
        await Problem(await Send(_c, HttpMethod.Get, $"/api/v1/terms/{termId}", other), HttpStatusCode.NotFound, "resource.not_found");
        // A demo term id cannot be smuggled into the other tenant's class.
        var subject = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/subjects", other, new { code = "M", name = "M" }), HttpStatusCode.Created);
        var course = await Ok(await Send(_c, HttpMethod.Post, "/api/v1/courses", other, new { subjectId = Id(subject), code = "M1", name = "M1" }), HttpStatusCode.Created);
        var res = await Send(_c, HttpMethod.Post, "/api/v1/classes", other, new { termId, courseId = Id(course), branchId = Guid.NewGuid(), code = "X", name = "X", capacity = 5 });
        Assert.True(res.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden, res.StatusCode.ToString());
        // Demo still has the built-in schemes only once.
        Assert.Single((await Get(_c, "/api/v1/grading-schemes", demoAdmin)).AsArray(), s => s!["code"]!.GetValue<string>() == "IR20");
    }

    [Fact]
    public async Task Concurrent_enrollments_cannot_exceed_capacity()
    {
        var x = await FreshAsync();
        var cls = await ClassAsync(x, x.North, x.Manager, capacity: 1);
        var students = new List<Guid>();
        for (var i = 0; i < 4; i++) students.Add(await NewStudentAsync(x.Manager, x.North));
        var results = await Task.WhenAll(students.Select(s => Send(_c, HttpMethod.Post, "/api/v1/enrollments", x.Manager, new { classId = Id(cls), studentId = s })));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Single((await Get(_c, $"/api/v1/classes/{Id(cls)}/roster", x.Manager)).AsArray());
        Assert.Equal(1, (await Get(_c, $"/api/v1/classes/{Id(cls)}", x.Manager))["enrolledCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task Academic_errors_are_localized()
    {
        var x = await FreshAsync();
        var yearId = (await Get(_c, $"/api/v1/terms/{x.TermId}", x.Admin))["academicYearId"]!.GetValue<string>();
        foreach (var (lang, expected) in new[] { ("en", "terms"), ("fa", "نیمسال") })
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/academic-years/{yearId}");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", x.Admin);
            req.Headers.AcceptLanguage.ParseAdd(lang);
            var res = await _c.SendAsync(req);
            var body = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
            Assert.Equal("academic.year_in_use", body["code"]!.GetValue<string>());
            Assert.Contains(expected, body["title"]!.GetValue<string>());
        }
    }
}
