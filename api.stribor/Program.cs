using Microsoft.EntityFrameworkCore;
using api.stribor;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;

#region "Builder"
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<StriborDb>(options =>
    options.UseMySql(connectionString: builder.Configuration.GetConnectionString("stribor"),
                     serverVersion: ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("stribor"))));
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseHttpsRedirection();

#endregion

#region "Sink"
app.MapGet("/sink/{planId}", async (string planId, StriborDb db, IConfiguration cfg) =>
{
    if(String.IsNullOrEmpty(planId))
        return Results.BadRequest();

    var plan = await db.Plan.FirstOrDefaultAsync(w => w.PlanId == planId);

    if (plan == null)
        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);

    var workouts = await db.Workout.Where(w => w.PlanId == planId).ToListAsync();
    var hshWorkouts = workouts.Select(w => w.WorkoutId);

    var sets = await db.Set.Where(w => hshWorkouts.Contains(w.WorkoutId)).ToListAsync();
    var hshSets = sets.Select(w => w.SetId);

    var exercises = await db.Exercise
                            .Join(db.SetExercise,
                                  ex => ex.ExerciseId,
                                  se => se.ExerciseId,
                                  (ex, se) => new
                                  {
                                        se.SetId,
                                        se.Order,
                                        ex.ExerciseId,
                                        ex.Name,
                                        ex.Description,
                                        ex.ImageUrl
                                  })
                            .Where(w => hshSets.Contains(w.SetId))
                            .ToListAsync();
    var hshExercises = exercises.Select(w => w.ExerciseId);

    var muscles = await db.Muscle
                .Join(db.ExerciseMuscle,
                        m => m.MuscleId,
                        ex => ex.MuscleId,
                        (m, ex) => new
                        {
                            m.MuscleId,
                            m.MuscleCategoryId,
                            ex.ExerciseId,
                            m.Name
                        })
                 .Where(w => hshExercises.Contains(w.ExerciseId))
                 .ToListAsync();

    var planList = new PlanList() { PlanId = planId, Name = plan.Name };
    planList.Workouts = workouts.Select(s => new WorkoutList()
    {
        WorkoutId = s.WorkoutId,
        Name = s.Name,
        Sets = sets.Where(w => w.WorkoutId == s.WorkoutId)
                   .OrderBy(o => o.Order)
                   .Select(s => new SetList()
                   {
                       SetId = s.SetId,
                       Name = s.Name,
                       Exercises = exercises.Where(w => w.SetId == s.SetId)
                                            .OrderBy(o => o.Order)
                                            .Select(s => new ExerciseList
                                             {
                                                 ExerciseId = s.ExerciseId,
                                                 Name = s.Name,
                                                 Muscles = muscles.Where(w => w.ExerciseId == s.ExerciseId)
                                                                  .Select(s => new Muscle
                                                                  {
                                                                        MuscleId = s.MuscleId,
                                                                        MuscleCategoryId = s.MuscleCategoryId,
                                                                        Name = s.Name
                                                                  }).ToList()
                                             }).ToList()
                   }).ToList()
    }).ToList();

    return Results.Ok(planList);
})
.WithName("GetSink")
.WithTags("Sink")
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces<List<Plan>>(StatusCodes.Status200OK);

#endregion

#region "Plan"
app.MapGet("/plan", async (StriborDb db, IConfiguration cfg) =>
{
    var plans = await db.Plan.ToListAsync();

    return Results.Ok(plans);
})
.WithName("GetAllPlans")
.WithTags("Plan")
.Produces<List<Plan>>(StatusCodes.Status200OK);

app.MapPost("/plan", async ([FromHeader(Name ="x-api-key")] string apiKey, Plan plan, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (plan == null)
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(plan.Name))
        return StatusCodes.Status400BadRequest;

    plan.PlanId = Guid.NewGuid().ToString();
    db.Plan.Add(plan);
    await db.SaveChangesAsync();
    return StatusCodes.Status201Created;
}) 
.WithName("AddPlan")
.WithTags("Plan")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces(StatusCodes.Status201Created);

app.MapPut("/plan/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, Plan plan, StriborDb db, IConfiguration cfg) =>
{
    if(String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (plan == null)
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(plan.Name))
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid planId))
        return StatusCodes.Status400BadRequest;
    if (!await db.Plan.AnyAsync(a => a.PlanId == id))
        return StatusCodes.Status416RangeNotSatisfiable;

    plan.PlanId = id;

    db.Plan.Update(plan);
    await db.SaveChangesAsync();
    return StatusCodes.Status202Accepted;

})
.WithName("UpdatePlan")
.WithTags("Plan")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces(StatusCodes.Status202Accepted);
#endregion

#region "Workout"
app.MapGet("/workout", async (StriborDb db, IConfiguration cfg) =>
{
    var workouts = await db.Workout.ToListAsync();

    return Results.Ok(workouts);
})
.WithName("GetAllWorkouts")
.WithTags("Workout")
.Produces<List<Workout>>(StatusCodes.Status200OK);

app.MapGet("/workout/{planId}", async (string planId, StriborDb db, IConfiguration cfg) =>
{
    if(String.IsNullOrEmpty(planId) || !Guid.TryParse(planId, out Guid guidPlanId))
        return Results.BadRequest();

    var workouts = await db.Workout
                           .Where(w => w.PlanId == planId)
                           .ToListAsync();

    return Results.Ok(workouts);
})
.WithName("GetPlanWorkouts")
.WithTags("Workout")
.ProducesProblem(StatusCodes.Status400BadRequest)
.Produces<List<Workout>>(StatusCodes.Status200OK);

app.MapPost("/workout", async ([FromHeader(Name = "x-api-key")] string apiKey, Workout workout, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (workout == null)
        return StatusCodes.Status400BadRequest;
    if (workout.PlanId is null || !Guid.TryParse(workout.PlanId.ToString(), out Guid planId))
        return StatusCodes.Status400BadRequest;
    if (!await db.Plan.AnyAsync(a => a.PlanId == workout.PlanId))
        return StatusCodes.Status417ExpectationFailed;

    workout.WorkoutId = Guid.NewGuid().ToString();

    db.Workout.Add(workout);
    await db.SaveChangesAsync();
    return StatusCodes.Status201Created;
})
.WithName("AddWorkout")
.WithTags("Workout")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status417ExpectationFailed)
.Produces(StatusCodes.Status201Created);

app.MapPut("/workout/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, Workout workout, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (workout == null)
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(workout.Name))
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid workoutId))
        return StatusCodes.Status400BadRequest;
    if (!await db.Workout.AnyAsync(a => a.WorkoutId == id))
        return StatusCodes.Status416RangeNotSatisfiable;
    if (!await db.Plan.AnyAsync(a => a.PlanId == workout.PlanId))
        return StatusCodes.Status417ExpectationFailed;

    workout.WorkoutId = id;

    db.Workout.Update(workout);
    await db.SaveChangesAsync();
    return StatusCodes.Status202Accepted;

})
.WithName("UpdateWorkout")
.WithTags("Workout")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.ProducesValidationProblem(StatusCodes.Status417ExpectationFailed)
.Produces(StatusCodes.Status202Accepted);

#endregion

#region "Set"
app.MapGet("/set", async (StriborDb db, IConfiguration cfg) =>
{
    var sets = await db.Set.OrderBy(o => o.Order).ToListAsync();

    return Results.Ok(sets);
})
.WithName("GetAllSets")
.WithTags("Set")
.Produces<List<Set>>(StatusCodes.Status200OK);

app.MapGet("/set/{workoutId}", async (string workoutId, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(workoutId) || !Guid.TryParse(workoutId, out Guid guidWorkoutId))
        return Results.BadRequest();

    var sets = await db.Set.Where(w => w.WorkoutId == workoutId)
                           .OrderBy(o => o.Order)
                           .ToListAsync();

    return Results.Ok(sets);
})
.WithName("GetWorkoutSets")
.WithTags("Set")
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.Produces<List<Set>>(StatusCodes.Status200OK);


app.MapPost("/set", async ([FromHeader(Name = "x-api-key")] string apiKey, Set set, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (set == null)
        return StatusCodes.Status400BadRequest;
    if (set.WorkoutId is null || !Guid.TryParse(set.WorkoutId.ToString(), out Guid workoutId))
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(set.Name))
        return StatusCodes.Status400BadRequest;
    if (!await db.Workout.AnyAsync(a => a.WorkoutId == set.WorkoutId))
        return StatusCodes.Status417ExpectationFailed;

    set.SetId = Guid.NewGuid().ToString();

    db.Set.Add(set);
    await db.SaveChangesAsync();
    return StatusCodes.Status201Created;
})
.WithName("AddSet")
.WithTags("Set")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.ProducesValidationProblem(StatusCodes.Status417ExpectationFailed)
.Produces(StatusCodes.Status201Created);

app.MapPut("/set/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, Set set, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (set == null)
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(set.Name))
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid setId))
        return StatusCodes.Status400BadRequest;
    if (!await db.Set.AnyAsync(a => a.SetId == id))
        return StatusCodes.Status416RangeNotSatisfiable;
    if (!await db.Workout.AnyAsync(a => a.WorkoutId == set.WorkoutId))
        return StatusCodes.Status417ExpectationFailed;

    set.SetId = id;

    db.Set.Update(set);
    await db.SaveChangesAsync();
    return StatusCodes.Status202Accepted;

})
.WithName("UpdateSet")
.WithTags("Set")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.ProducesValidationProblem(StatusCodes.Status417ExpectationFailed)
.Produces(StatusCodes.Status202Accepted);
#endregion

#region "SetExcercises"
app.MapGet("/set-exercises/{setId}", async (string setId, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(setId) || !Guid.TryParse(setId, out Guid guidSetId))
        return Results.BadRequest();

    var setExercises = await db.SetExercise
                                .Join(db.Set,
                                        se=>se.SetId,
                                        set=>set.SetId,
                                        (se,set) => new
                                        {
                                            se.SetId,
                                            set.Name,
                                            se.ExerciseId,
                                            se.Order,
                                            se.Duration,
                                            se.Count,
                                            se.Unit
                                        })
                                .Join(db.Exercise,
                                    set=>set.ExerciseId,
                                    ex=>ex.ExerciseId,
                                    (set,ex) => new SetExerciseList()
                                    {
                                        SetId = set.SetId,
                                        SetName = set.Name,
                                        ExerciseId = set.ExerciseId,
                                        Order = set.Order, 
                                        Duration = set.Duration,
                                        Unit = set.Unit,
                                        Count = set.Count,
                                        ExerciseName = ex.Name,
                                        Description = ex.Description,
                                        ImageUrl = ex.ImageUrl
                                    })
                                .Where(w => w.SetId == setId)
                                .OrderBy(o => o.Order)
                                .ToListAsync();

    return Results.Ok(setExercises);
})
.WithName("GetSetExercises")
.WithTags("Set exercises")
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.Produces<List<SetExerciseList>>(StatusCodes.Status200OK);

app.MapPost("/set-exercises", async ([FromHeader(Name = "x-api-key")] string apiKey, List<SetExercise> setExercises, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (setExercises == null)
        return StatusCodes.Status400BadRequest;

    var hshSetIds = new HashSet<string>();
    foreach(SetExercise setExercise in setExercises)
    {
        if (setExercise == null)
            return StatusCodes.Status400BadRequest;
        if (String.IsNullOrEmpty(setExercise.SetId) || !Guid.TryParse(setExercise.SetId, out Guid setId))
            return StatusCodes.Status400BadRequest;
        if (String.IsNullOrEmpty(setExercise.ExerciseId) || !Guid.TryParse(setExercise.ExerciseId, out Guid exerciseId))
            return StatusCodes.Status400BadRequest;
        if (!await db.Set.AnyAsync(a => a.SetId == setExercise.SetId))
            return StatusCodes.Status417ExpectationFailed;
        if (!await db.Exercise.AnyAsync(a => a.ExerciseId == setExercise.ExerciseId))
            return StatusCodes.Status417ExpectationFailed;
        hshSetIds.Add(setExercise.SetId);
        setExercise.Id = Guid.NewGuid().ToString();
    }

    db.RemoveRange(db.SetExercise.Where(w => hshSetIds.Contains(w.SetId)));
    db.SetExercise.AddRange(setExercises);
    await db.SaveChangesAsync();
    return StatusCodes.Status201Created;
})
.WithName("AddSetExercises")
.WithTags("Set exercises")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status417ExpectationFailed)
.Produces(StatusCodes.Status201Created);
#endregion

#region "Excercise"
app.MapGet("/excercise", async (StriborDb db, IConfiguration cfg) =>
{
    var excercises = await db.Exercise.ToListAsync();

    return Results.Ok(excercises);
})
.WithName("GetAllExercises")
.WithTags("Excercise")
.Produces<List<Exercise>>(StatusCodes.Status200OK);

app.MapPost("/excercise", async ([FromHeader(Name = "x-api-key")] string apiKey, Exercise exercise, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (exercise == null)
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(exercise.Name))
        return StatusCodes.Status400BadRequest;

    exercise.ExerciseId = Guid.NewGuid().ToString();

    db.Exercise.Add(exercise);
    await db.SaveChangesAsync();
    return StatusCodes.Status201Created;
})
.WithName("AddExcercise")
.WithTags("Excercise")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status201Created);

app.MapPut("/excercise/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, Exercise exercise, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (exercise == null)
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(exercise.Name))
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid excerciseId))
        return StatusCodes.Status400BadRequest;
    if (!await db.Exercise.AnyAsync(a => a.ExerciseId == id))
        return StatusCodes.Status416RangeNotSatisfiable;

    exercise.ExerciseId = id;

    db.Exercise.Update(exercise);
    await db.SaveChangesAsync();
    return StatusCodes.Status202Accepted;

})
.WithName("UpdateExcercise")
.WithTags("Excercise")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces(StatusCodes.Status202Accepted);
#endregion

#region "ExerciseMuscles"
app.MapGet("/exercise-muscles/{muscleId}", async (string muscleId, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(muscleId))
        return Results.BadRequest();

    var setExercises = await db.ExerciseMuscle.Where(w => w.MuscleId == muscleId).ToListAsync();

    return Results.Ok(setExercises);
})
.WithName("GetExerciseMuscles")
.WithTags("Exercise muscles")
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.Produces<List<Plan>>(StatusCodes.Status200OK);

app.MapPost("/exercise-muscles", async ([FromHeader(Name = "x-api-key")] string apiKey, List<ExerciseMuscle> exerciseMuscles, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (exerciseMuscles == null)
        return StatusCodes.Status400BadRequest;

    var hshExerciseIds = new HashSet<string>();
    foreach (ExerciseMuscle exerciseMuscle in exerciseMuscles)
    {
        if (exerciseMuscle == null)
            return StatusCodes.Status400BadRequest;
        if (String.IsNullOrEmpty(exerciseMuscle.ExerciseId) || !Guid.TryParse(exerciseMuscle.ExerciseId, out Guid setId))
            return StatusCodes.Status400BadRequest;
        if (String.IsNullOrEmpty(exerciseMuscle.ExerciseId) || !Guid.TryParse(exerciseMuscle.ExerciseId, out Guid exerciseId))
            return StatusCodes.Status400BadRequest;
        if (!await db.Muscle.AnyAsync(a => a.MuscleId == exerciseMuscle.MuscleId))
            return StatusCodes.Status417ExpectationFailed;
        if (!await db.Exercise.AnyAsync(a => a.ExerciseId == exerciseMuscle.ExerciseId))
            return StatusCodes.Status417ExpectationFailed;

        hshExerciseIds.Add(exerciseMuscle.ExerciseId);
        exerciseMuscle.Id = Guid.NewGuid().ToString();
    }

    db.RemoveRange(db.ExerciseMuscle.Where(w => hshExerciseIds.Contains(w.ExerciseId)));
    db.ExerciseMuscle.AddRange(exerciseMuscles);

    await db.SaveChangesAsync();

    return StatusCodes.Status201Created;
})
.WithName("UpdateExerciseMuscles")
.WithTags("Exercise muscles")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status417ExpectationFailed)
.Produces(StatusCodes.Status201Created);
#endregion

#region "Muscle"
app.MapGet("/muscle", async (StriborDb db, IConfiguration cfg) =>
{
    var muscles = await db.Muscle.ToListAsync();

    return Results.Ok(muscles);
})
.WithName("GetAllMuscles")
.WithTags("Muscle")
.Produces<List<Muscle>>(StatusCodes.Status200OK);

app.MapPost("/muscle", async ([FromHeader(Name = "x-api-key")] string apiKey, Muscle muscle, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (muscle == null)
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(muscle.Name))
        return StatusCodes.Status400BadRequest;
    if (muscle.MuscleCategoryId is null || !Guid.TryParse(muscle.MuscleCategoryId.ToString(), out Guid PlanId))
        return StatusCodes.Status400BadRequest;
    if (!await db.MuscleCategory.AnyAsync(a => a.MuscleCategoryId == muscle.MuscleCategoryId))

        return StatusCodes.Status417ExpectationFailed;
    muscle.MuscleId = Guid.NewGuid().ToString();

    db.Muscle.Add(muscle);
    await db.SaveChangesAsync();
    return StatusCodes.Status201Created;
})
.WithName("AddMuscle")
.WithTags("Muscle")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.ProducesValidationProblem(StatusCodes.Status417ExpectationFailed)
.Produces(StatusCodes.Status201Created);

app.MapPut("/muscle/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, Muscle muscle, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (muscle == null)
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(muscle.Name))
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid setId))
        return StatusCodes.Status400BadRequest;
    if (!await db.Muscle.AnyAsync(a => a.MuscleId == id))
        return StatusCodes.Status416RangeNotSatisfiable;
    if (!await db.MuscleCategory.AnyAsync(a => a.MuscleCategoryId == muscle.MuscleCategoryId))
        return StatusCodes.Status417ExpectationFailed;

    muscle.MuscleId = id;

    db.Muscle.Update(muscle);
    await db.SaveChangesAsync();
    return StatusCodes.Status202Accepted;

})
.WithName("UpdateMuscle")
.WithTags("Muscle")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.ProducesValidationProblem(StatusCodes.Status417ExpectationFailed)
.Produces(StatusCodes.Status202Accepted);

#endregion

#region "MuscleCategory"
app.MapGet("/muscle-category", async (StriborDb db, IConfiguration cfg) =>
{
    var muscleCategories = await db.MuscleCategory.ToListAsync();

    return Results.Ok(muscleCategories);
})
.WithName("GetAllMuscleCategories")
.WithTags("Muscle category")
.Produces<List<MuscleCategory>>(StatusCodes.Status200OK);

app.MapPost("/muscle-category", async ([FromHeader(Name = "x-api-key")] string apiKey, MuscleCategory muscleCategory, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (muscleCategory == null)
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(muscleCategory.Name))
        return StatusCodes.Status400BadRequest;
    if (await db.MuscleCategory.AnyAsync(a => a.Name == muscleCategory.Name))
        return StatusCodes.Status416RequestedRangeNotSatisfiable;

    muscleCategory.MuscleCategoryId = Guid.NewGuid().ToString();

    db.MuscleCategory.Add(muscleCategory);
    await db.SaveChangesAsync();
    return StatusCodes.Status201Created;
})
.WithName("AddMuscleCategory")
.WithTags("Muscle category")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces(StatusCodes.Status201Created);

app.MapPut("/muscle-category/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, MuscleCategory muscleCategory, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (muscleCategory == null)
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(muscleCategory.Name))
        return StatusCodes.Status400BadRequest;
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid muscleCategoryId))
        return StatusCodes.Status400BadRequest;
    if (!await db.MuscleCategory.AnyAsync(a => a.MuscleCategoryId == id))
        return StatusCodes.Status416RangeNotSatisfiable;

    muscleCategory.MuscleCategoryId = id;

    db.MuscleCategory.Update(muscleCategory);
    await db.SaveChangesAsync();
    return StatusCodes.Status202Accepted;

})
.WithName("UpdateMuscleCategory")
.WithTags("Muscle category")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces(StatusCodes.Status202Accepted);
#endregion

app.Run();

#region "Db context"

class StriborDb : DbContext
{
    public StriborDb(DbContextOptions<StriborDb> options)
        : base(options) { }

    public DbSet<Plan> Plan => Set<Plan>();
    public DbSet<Workout> Workout => Set<Workout>();
    public DbSet<Set> Set => Set<Set>();
    public DbSet<SetExercise> SetExercise => Set<SetExercise>();
    public DbSet<Exercise> Exercise => Set<Exercise>();
    public DbSet<Muscle> Muscle => Set<Muscle>();
    public DbSet<MuscleCategory> MuscleCategory => Set<MuscleCategory>();
    public DbSet<ExerciseMuscle> ExerciseMuscle => Set<ExerciseMuscle>();
    
}
#endregion