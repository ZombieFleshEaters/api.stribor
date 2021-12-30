
using System.ComponentModel.DataAnnotations;

namespace api.stribor
{

    #region "Models"

    public class Plan
    {
        [Required]
        [Key]
        public string PlanId { get; set; }

        [Required]
        public string Name { get; set; }
        public string Description { get; set; } 
    }

    public class Workout
    {
        [Required]
        [Key]
        public string WorkoutId { get; set; }

        [Required]
        public string PlanId { get; set; }
        [Required]
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public class Set
    {
        [Required]
        [Key]
        public string SetId { get; set; }

        [Required]
        public string WorkoutId { get; set; }
        [Required]
        public string Name { get; set; }
        public int Order { get; set; }

    }

    public class SetExercise
    {
        [Required]
        [Key]
        public string Id { get; set; }

        [Required]
        public string SetId { get; set; }

        [Required]
        public string ExerciseId { get; set; }
        public int Order { get; set; }
        public string Duration { get; set; }
        public string Unit { get; set; }
        public int Count { get; set; }

    }

    public class Exercise
    {
        [Required]
        [Key]
        public string ExerciseId { get; set; }
        [Required]
        public string Name { get; set; }
        public string Description { get; set; }

        [DataType(DataType.ImageUrl)]
        public string ImageUrl { get; set; }
    }

    public class ExerciseMuscle
    {
        [Required]
        [Key]
        public string Id { get; set; }

        [Required]
        public string ExerciseId { get; set; }

        [Required]
        public string MuscleId { get; set; }

    }

    public class Muscle
    {
        [Required]
        [Key]
        public string MuscleId { get; set; }
        [Required]
        public string MuscleCategoryId { get; set; }
        [Required]
        public string Name { get; set; }

    }

    public class MuscleCategory
    {
        [Required]
        [Key]
        public string MuscleCategoryId { get; set; }
        [Required]
        public string Name { get; set; }
    }
    #endregion

    #region "Sink list"
    public class PlanList
    {
        public string PlanId { get; set; }
        public string Name { get; set; }
        public List<WorkoutList> Workouts { get; set; }
    }

    public class WorkoutList
    {
        public string WorkoutId { get; set; }
        public string Name { get; set; }
        public List<SetList> Sets { get; set; }
    }

    public class SetList
    {
        public string SetId { get; set; }
        public string Name { get; set; }
        public List<ExerciseList> Exercises { get; set; }
    }

    public class ExerciseList
    {
        public string ExerciseId { get; set; }
        public string Name { get; set; }
        public List<Muscle> Muscles { get; set; }
    }

#endregion

}
