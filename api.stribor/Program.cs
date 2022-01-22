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
        return Results.Unauthorized();
    if (plan == null)
        return Results.BadRequest();
    if (String.IsNullOrEmpty(plan.Name))
        return Results.BadRequest();

    plan.PlanId = Guid.NewGuid().ToString();
    db.Plan.Add(plan);
    await db.SaveChangesAsync();

    return Results.Created($"/plan/{plan.PlanId}", plan);
}) 
.WithName("AddPlan")
.WithTags("Plan")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces<Plan>(StatusCodes.Status201Created);

app.MapPut("/plan/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, Plan plan, StriborDb db, IConfiguration cfg) =>
{
    if(String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return Results.Unauthorized();
    if (plan == null)
        return Results.BadRequest();
    if (String.IsNullOrEmpty(plan.Name))
        return Results.BadRequest();
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid planId))
        return Results.BadRequest();
    if (!await db.Plan.AnyAsync(a => a.PlanId == id))
        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);

    plan.PlanId = id;

    db.Plan.Update(plan);
    await db.SaveChangesAsync();
    return Results.Accepted($"/plan/{plan.PlanId}", plan);
})
.WithName("UpdatePlan")
.WithTags("Plan")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces(StatusCodes.Status202Accepted);

app.MapDelete("/plan/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return Results.Unauthorized();
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid planId))
        return Results.BadRequest();

    var plan = await db.Plan.FirstOrDefaultAsync(f => f.PlanId == id);

    if (plan == null)
        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);

    db.Plan.Remove(plan);
    await db.SaveChangesAsync();

    return Results.Ok();
})
.WithName("DeletePlan")
.WithTags("Plan")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces(StatusCodes.Status200OK);
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
        return Results.Unauthorized();
    if (workout == null)
        return Results.BadRequest();
    if (workout.PlanId is null || !Guid.TryParse(workout.PlanId.ToString(), out Guid planId))
        return Results.BadRequest();
    if (!await db.Plan.AnyAsync(a => a.PlanId == workout.PlanId))
        return Results.StatusCode(StatusCodes.Status417ExpectationFailed);

    workout.WorkoutId = Guid.NewGuid().ToString();

    db.Workout.Add(workout);
    await db.SaveChangesAsync();
    return Results.Created($"/workout/{workout.WorkoutId}",workout);
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
        return Results.Unauthorized();
    if (workout == null)
        return Results.BadRequest();
    if (String.IsNullOrEmpty(workout.Name))
        return Results.BadRequest();
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid workoutId))
        return Results.BadRequest();
    if (!await db.Workout.AnyAsync(a => a.WorkoutId == id))
        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);
    if (!await db.Plan.AnyAsync(a => a.PlanId == workout.PlanId))
        return Results.StatusCode(StatusCodes.Status417ExpectationFailed);

    workout.WorkoutId = id;

    db.Workout.Update(workout);
    await db.SaveChangesAsync();
    return Results.Accepted($"/workout/{workout.WorkoutId}", workout);

})
.WithName("UpdateWorkout")
.WithTags("Workout")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.ProducesValidationProblem(StatusCodes.Status417ExpectationFailed)
.Produces(StatusCodes.Status202Accepted);

app.MapDelete("/workout/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return Results.Unauthorized();
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid workoutId))
        return Results.BadRequest();

    var workout = await db.Workout.FirstOrDefaultAsync(f => f.WorkoutId == id);

    if (workout == null)
        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);

    db.Workout.Remove(workout);

    await db.SaveChangesAsync();
    return Results.Ok();

})
.WithName("DeleteWorkout")
.WithTags("Workout")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces(StatusCodes.Status200OK);

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
        return Results.Unauthorized();
    if (set == null)
        return Results.BadRequest();
    if (set.WorkoutId is null || !Guid.TryParse(set.WorkoutId.ToString(), out Guid workoutId))
        return Results.BadRequest();
    if (String.IsNullOrEmpty(set.Name))
        return Results.BadRequest();
    if (!await db.Workout.AnyAsync(a => a.WorkoutId == set.WorkoutId))
        return Results.StatusCode(StatusCodes.Status417ExpectationFailed);

    set.SetId = Guid.NewGuid().ToString();

    db.Set.Add(set);
    await db.SaveChangesAsync();
    return Results.Created($"/set/{set.SetId}",set);
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
        return Results.Unauthorized();
    if (set == null)
        return Results.BadRequest();
    if (String.IsNullOrEmpty(set.Name))
        return Results.BadRequest();
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid setId))
        return Results.BadRequest();
    if (!await db.Set.AnyAsync(a => a.SetId == id))
        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);
    if (!await db.Workout.AnyAsync(a => a.WorkoutId == set.WorkoutId))
        return Results.StatusCode(StatusCodes.Status417ExpectationFailed);

    set.SetId = id;

    db.Set.Update(set);
    await db.SaveChangesAsync();
    return Results.Accepted($"/set/{set.SetId}",set);

})
.WithName("UpdateSet")
.WithTags("Set")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.ProducesValidationProblem(StatusCodes.Status417ExpectationFailed)
.Produces(StatusCodes.Status202Accepted);

app.MapDelete("/set/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return Results.Unauthorized();
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid setId))
        return Results.BadRequest();

    var set = await db.Set.FirstOrDefaultAsync(f => f.SetId == id);

    if (set == null)
        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);

    db.Set.Remove(set);
    await db.SaveChangesAsync();
    return Results.Ok();
})
.WithName("DeleteSet")
.WithTags("Set")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces(StatusCodes.Status200OK);
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
                                            se.Id,
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
                                        Id = set.Id,
                                        SetId = set.SetId,
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

app.MapPost("/set-exercises/{setId}", async ([FromHeader(Name = "x-api-key")] string apiKey, string setId, SetExercise setExercise, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return Results.Unauthorized();

    if (setExercise == null)
        return Results.BadRequest();
    if (String.IsNullOrEmpty(setExercise.SetId) || !Guid.TryParse(setExercise.SetId, out Guid guidSetId))
        return Results.BadRequest();
    if (String.IsNullOrEmpty(setExercise.ExerciseId) || !Guid.TryParse(setExercise.ExerciseId, out Guid exerciseId))
        return Results.BadRequest();
    if (!await db.Set.AnyAsync(a => a.SetId == setExercise.SetId))
        return Results.StatusCode(StatusCodes.Status417ExpectationFailed);

    var exercise = await db.Exercise.FirstOrDefaultAsync(f => f.ExerciseId == setExercise.ExerciseId);

    if (exercise == null)
        return Results.StatusCode(StatusCodes.Status417ExpectationFailed);

    setExercise.Id = Guid.NewGuid().ToString();

    db.SetExercise.Add(setExercise);
    await db.SaveChangesAsync();

    var result = new SetExerciseList()
    {
        ExerciseId = setExercise.ExerciseId,
        Id = setExercise.Id,
        Unit = setExercise.Unit,
        Count = setExercise.Count,
        Duration = setExercise.Duration,
        ExerciseName = exercise.Name,
        Description = exercise.Description,
        ImageUrl = exercise.ImageUrl,
        Order = setExercise.Order,
        SetId = setExercise.SetId
    };

    return Results.Created($"/set-exercises/{setExercise.SetId}/{setExercise.Id}", result);
})
.WithName("UpdateSetExercise")
.WithTags("Set exercises")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status417ExpectationFailed)
.Produces<SetExerciseList>(StatusCodes.Status201Created);

app.MapPut("/set-exercises/{setId}/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string setId, string id, SetExercise setExercise, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return Results.Unauthorized();
    if (setExercise == null)
        return Results.BadRequest();
    if (String.IsNullOrEmpty(setExercise.SetId) || !Guid.TryParse(setExercise.SetId, out Guid guidSetId))
        return Results.BadRequest();
    if (String.IsNullOrEmpty(setExercise.ExerciseId) || !Guid.TryParse(setExercise.ExerciseId, out Guid exerciseId))
        return Results.BadRequest();
    if(!await db.SetExercise.AnyAsync(a => a.Id == id))
        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);
    if (!await db.Set.AnyAsync(a => a.SetId == setExercise.SetId))
        return Results.StatusCode(StatusCodes.Status417ExpectationFailed);

    var exercise = await db.Exercise.FirstOrDefaultAsync(f => f.ExerciseId == setExercise.ExerciseId);

    if (exercise == null)
        return Results.StatusCode(StatusCodes.Status417ExpectationFailed);

    db.SetExercise.Update(setExercise);
    await db.SaveChangesAsync();

    var result = new SetExerciseList()
    {
        ExerciseId = setExercise.ExerciseId,
        Id = setExercise.Id,
        Unit = setExercise.Unit,
        Count = setExercise.Count,
        Duration = setExercise.Duration,
        ExerciseName = exercise.Name,
        Description = exercise.Description,
        ImageUrl = exercise.ImageUrl,
        Order = setExercise.Order,
        SetId = setExercise.SetId
    };

    return Results.Created($"/set-exercises/{setExercise.SetId}/{setExercise.Id}", result);
})
.WithName("AddSetExercise")
.WithTags("Set exercises")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.ProducesValidationProblem(StatusCodes.Status417ExpectationFailed)
.Produces<SetExerciseList>(StatusCodes.Status201Created);

app.MapDelete("/set-exercises/{setId}/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string setId, string id, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return Results.Unauthorized();
    if (String.IsNullOrEmpty(setId) || !Guid.TryParse(setId, out Guid guidSetId))
        return Results.BadRequest();
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid setExerciseId))
        return Results.BadRequest();

    var setExercise = await db.SetExercise.FirstOrDefaultAsync(f => f.Id == id);

    if (setExercise == null)
        return Results.StatusCode(StatusCodes.Status417ExpectationFailed);

    db.SetExercise.Remove(setExercise);
    await db.SaveChangesAsync();
    return Results.Ok();
})
.WithName("DeleteSetExercise")
.WithTags("Set exercises")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status417ExpectationFailed)
.Produces(StatusCodes.Status200OK);
#endregion

#region "Excercise"
app.MapGet("/exercise", async (StriborDb db, IConfiguration cfg) =>
{
    var exercises = await db.Exercise.ToListAsync();

    exercises.OrderBy(o => o.Name);

    return Results.Ok(exercises);
})
.WithName("GetAllExercises")
.WithTags("Exercise")
.Produces<List<Exercise>>(StatusCodes.Status200OK);

app.MapPost("/exercise", async ([FromHeader(Name = "x-api-key")] string apiKey, Exercise exercise, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return Results.Unauthorized();
    if (exercise == null)
        return Results.BadRequest();
    if (String.IsNullOrEmpty(exercise.Name))
        return Results.BadRequest();

    exercise.ExerciseId = Guid.NewGuid().ToString();

    db.Exercise.Add(exercise);
    await db.SaveChangesAsync();
    return Results.Created($"/exercise/{exercise.ExerciseId}", exercise);
})
.WithName("AddExercise")
.WithTags("Exercise")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.Produces<Exercise>(StatusCodes.Status201Created);

app.MapPut("/exercise/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, Exercise exercise, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return Results.Unauthorized();
    if (exercise == null)
        return Results.BadRequest();
    if (String.IsNullOrEmpty(exercise.Name))
        return Results.BadRequest();
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid excerciseId))
        return Results.BadRequest();
    if (!await db.Exercise.AnyAsync(a => a.ExerciseId == id))
        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);

    exercise.ExerciseId = id;

    db.Exercise.Update(exercise);
    await db.SaveChangesAsync();
    return Results.Accepted($"/exercise/{exercise.ExerciseId}", exercise);

})
.WithName("UpdateExercise")
.WithTags("Exercise")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces<Exercise>(StatusCodes.Status202Accepted);

app.MapDelete("/exercise/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return Results.Unauthorized();
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid excerciseId))
        return Results.BadRequest();

    var exercise = await db.Exercise.FirstOrDefaultAsync(f => f.ExerciseId == id);

    if (exercise == null)
        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);

    db.Exercise.Remove(exercise);
    await db.SaveChangesAsync();
    return Results.Ok();
})
.WithName("DeleteExercise")
.WithTags("Exercise")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces(StatusCodes.Status200OK);
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
        return Results.Unauthorized();
    if (exerciseMuscles == null)
        return Results.BadRequest();

    var hshExerciseIds = new HashSet<string>();
    foreach (ExerciseMuscle exerciseMuscle in exerciseMuscles)
    {
        if (exerciseMuscle == null)
            return Results.BadRequest();
        if (String.IsNullOrEmpty(exerciseMuscle.ExerciseId) || !Guid.TryParse(exerciseMuscle.ExerciseId, out Guid setId))
            return Results.BadRequest();
        if (String.IsNullOrEmpty(exerciseMuscle.ExerciseId) || !Guid.TryParse(exerciseMuscle.ExerciseId, out Guid exerciseId))
            return Results.BadRequest();
        if (!await db.Muscle.AnyAsync(a => a.MuscleId == exerciseMuscle.MuscleId))
            return Results.StatusCode(StatusCodes.Status417ExpectationFailed);
        if (!await db.Exercise.AnyAsync(a => a.ExerciseId == exerciseMuscle.ExerciseId))
            return Results.StatusCode(StatusCodes.Status417ExpectationFailed);

        hshExerciseIds.Add(exerciseMuscle.ExerciseId);
        exerciseMuscle.Id = Guid.NewGuid().ToString();
    }

    db.RemoveRange(db.ExerciseMuscle.Where(w => hshExerciseIds.Contains(w.ExerciseId)));
    db.ExerciseMuscle.AddRange(exerciseMuscles);

    await db.SaveChangesAsync();

    return Results.Created("/exercise-muscles", exerciseMuscles);
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
        return Results.Unauthorized();
    if (muscle == null)
        return Results.BadRequest();
    if (String.IsNullOrEmpty(muscle.Name))
        return Results.BadRequest();
    if (muscle.MuscleCategoryId is null || !Guid.TryParse(muscle.MuscleCategoryId.ToString(), out Guid PlanId))
        return Results.BadRequest();
    if (!await db.MuscleCategory.AnyAsync(a => a.MuscleCategoryId == muscle.MuscleCategoryId))
        return Results.StatusCode(StatusCodes.Status417ExpectationFailed);

    muscle.MuscleId = Guid.NewGuid().ToString();

    db.Muscle.Add(muscle);
    await db.SaveChangesAsync();
    return Results.Created($"/muscle{muscle.MuscleId}", muscle);
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

app.MapDelete("/muscle/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return StatusCodes.Status401Unauthorized;
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid setId))
        return StatusCodes.Status400BadRequest;

    var muscle = await db.Muscle.FirstOrDefaultAsync(f => f.MuscleId == id);

    if (muscle == null)
        return StatusCodes.Status416RangeNotSatisfiable;

    db.Muscle.Remove(muscle);
    await db.SaveChangesAsync();
    return StatusCodes.Status200OK;
})
.WithName("DeleteMuscle")
.WithTags("Muscle")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces(StatusCodes.Status200OK);
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
        return Results.Unauthorized();
    if (muscleCategory == null)
        return Results.BadRequest();
    if (String.IsNullOrEmpty(muscleCategory.Name))
        return Results.BadRequest();
    if (await db.MuscleCategory.AnyAsync(a => a.Name == muscleCategory.Name))
        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);

    muscleCategory.MuscleCategoryId = Guid.NewGuid().ToString();

    db.MuscleCategory.Add(muscleCategory);
    await db.SaveChangesAsync();
    return Results.Created($"/muscle-category/{muscleCategory.MuscleCategoryId}", muscleCategory);
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
        return Results.Unauthorized();
    if (muscleCategory == null)
        return Results.BadRequest();
    if (String.IsNullOrEmpty(muscleCategory.Name))
        return Results.BadRequest();
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid muscleCategoryId))
        return Results.BadRequest();
    if (!await db.MuscleCategory.AnyAsync(a => a.MuscleCategoryId == id))
        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);

    muscleCategory.MuscleCategoryId = id;

    db.MuscleCategory.Update(muscleCategory);
    await db.SaveChangesAsync();
    return Results.Accepted($"/muscle-category/{muscleCategory.MuscleCategoryId}", muscleCategory);

})
.WithName("UpdateMuscleCategory")
.WithTags("Muscle category")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces(StatusCodes.Status202Accepted);

app.MapDelete("/muscle-category/{id}", async ([FromHeader(Name = "x-api-key")] string apiKey, string id, StriborDb db, IConfiguration cfg) =>
{
    if (String.IsNullOrEmpty(apiKey) || cfg["apiKey"] != apiKey)
        return Results.Unauthorized();
    if (String.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid muscleCategoryId))
        return Results.BadRequest();

    var muscleCategory = await db.MuscleCategory.FirstOrDefaultAsync(f => f.MuscleCategoryId == id);

    if (muscleCategory ==  null)
        return Results.StatusCode(StatusCodes.Status416RangeNotSatisfiable);

    db.MuscleCategory.Remove(muscleCategory);
    await db.SaveChangesAsync();
    return Results.Ok();

})
.WithName("DeleteMuscleCategory")
.WithTags("Muscle category")
.ProducesValidationProblem(StatusCodes.Status401Unauthorized)
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesValidationProblem(StatusCodes.Status416RangeNotSatisfiable)
.Produces(StatusCodes.Status200OK);
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