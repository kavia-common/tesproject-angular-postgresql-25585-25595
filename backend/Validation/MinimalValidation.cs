using System.ComponentModel.DataAnnotations;

namespace Backend.Validation;

public static class MinimalValidation
{
    // PUBLIC_INTERFACE
    public static IDictionary<string, string[]> Validate(object instance)
    {
        // Validate an object using DataAnnotations and return a property->errors dictionary.
        var results = new List<ValidationResult>();
        var ctx = new ValidationContext(instance);

        Validator.TryValidateObject(instance, ctx, results, validateAllProperties: true);

        var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in results)
        {
            var members = r.MemberNames?.Any() == true ? r.MemberNames : new[] { string.Empty };
            foreach (var m in members)
            {
                if (!dict.TryGetValue(m, out var list))
                {
                    list = new List<string>();
                    dict[m] = list;
                }

                if (!string.IsNullOrWhiteSpace(r.ErrorMessage))
                    list.Add(r.ErrorMessage!);
            }
        }

        return dict.ToDictionary(k => k.Key, v => v.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }
}
