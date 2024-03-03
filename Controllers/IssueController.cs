using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using trackingapi.Data;
using trackingapi.Models;
using Polly;
using Polly.Retry;
using System.Linq.Expressions;


namespace trackingapi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EntityController : ControllerBase
    {
        private readonly IssueDbContext _context;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly Random _random = new Random();

        public EntityController(IssueDbContext context)
        {
            _context = context;

            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }

        private bool ShouldSimulateFailure()
        {
            return _random.Next(1, 4) == 1;
        }




        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Create(Entity entity)
        {

            try
            {
                await _retryPolicy.ExecuteAsync(async () =>
                {
                    if (ShouldSimulateFailure())
                    {
                        Console.WriteLine($"Simulated failure in Create. Retrying...");
                        throw new Exception("Simulated failure.");
                    }

                    await _context.Entities.AddAsync(entity);
                    await _context.SaveChangesAsync();

                });

                
                return CreatedAtAction(nameof(GetById), new { id = entity.Id }, entity);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                return StatusCode(500, "Internal Server Error");
            }
        }

        /*[HttpGet]
        public async Task<IEnumerable<Entity>> Get()
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                if (ShouldSimulateFailure())
                {
                    Console.WriteLine("Simulated failure. Retrying...");
                    throw new Exception("Simulated failure.");
                }

                try
                {
                    return await _context.Entities.Include(e => e.Addresses)
                                                  .Include(e => e.Dates)
                                                  .Include(e => e.Names)
                                                  .ToListAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception: {ex.Message}");
                    throw;
                }
            });
        }
*/




        [HttpGet("{id}")]
        [ProducesResponseType(typeof(Entity), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(string id)
        {
            var entity = await _context.Entities.Include(e => e.Addresses)
                                                .Include(e => e.Dates)
                                                .Include(e => e.Names)
                                                .FirstOrDefaultAsync(e => e.Id == id);

            return entity == null ? NotFound() : Ok(entity);
        }


        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(string id)
        {
            try
            {
                // Retrieve the entity to be deleted along with related data
                var entityToDelete = await _context.Entities
                    .Include(e => e.Addresses)
                    .Include(e => e.Dates)
                    .Include(e => e.Names)
                    .FirstOrDefaultAsync(e => e.Id == id);

                if (entityToDelete == null)
                {
                    return NotFound("Entity not found");
                }

                // Explicitly set the state of each related entity to Deleted
                foreach (var address in entityToDelete.Addresses)
                {
                    _context.Entry(address).State = EntityState.Deleted;
                }

                foreach (var date in entityToDelete.Dates)
                {
                    _context.Entry(date).State = EntityState.Deleted;
                }

                foreach (var name in entityToDelete.Names)
                {
                    _context.Entry(name).State = EntityState.Deleted;
                }

                // Remove the main entity from the context
                _context.Entities.Remove(entityToDelete);

                // Save changes to the database
                await _context.SaveChangesAsync();

                return NoContent();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                throw;
            }
        }



        


        [HttpGet("entities")]
        [ProducesResponseType(typeof(IEnumerable<Entity>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEntities(
    [FromQuery] string search = null,
    [FromQuery(Name = "gender")] GenderType genderFilter = GenderType.All,
    [FromQuery] System.DateTime? startDate = null,
    [FromQuery] System.DateTime? endDate = null,
    [FromQuery] string[] countries = null,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 10,
    [FromQuery] string sortBy = "Id", // Default sorting by Id
    [FromQuery] string sortOrder = "asc") // Default sorting order is ascending
        {
            try
            {
                IQueryable<Entity> query = _context.Entities
                    .Include(e => e.Addresses)
                    .Include(e => e.Dates)
                    .Include(e => e.Names);

                // Apply search filter
                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(e =>
                        e.Addresses.Any(a => EF.Functions.Like(a.AddressLine, $"%{search}%")) ||
                        e.Names.Any(n => EF.Functions.Like(n.FirstName, $"%{search}%") || EF.Functions.Like(n.LastName, $"%{search}%"))
                    );
                }

                // Apply gender filter
                if (genderFilter != GenderType.All)
                {
                    query = query.Where(e => e.Gender == genderFilter.ToString());
                }

                // Apply date range filter
                if (startDate.HasValue)
                {
                    query = query.Where(e => e.Dates.Any(d => d.DateValue != null && d.DateValue.Value.Date >= startDate.Value.Date));
                }

                if (endDate.HasValue)
                {
                    query = query.Where(e => e.Dates.Any(d => d.DateValue != null && d.DateValue.Value.Date <= endDate.Value.Date));
                }

                // Apply country filter
                if (countries != null && countries.Length > 0)
                {
                    query = query.Where(e => e.Addresses.Any(a => countries.Contains(a.Country)));
                }

                // Sorting
                query = ApplySorting(query, sortBy, sortOrder);

                // Pagination
                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
                var entities = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

                var result = new
                {
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    CurrentPage = page,
                    PageSize = pageSize,
                    Entities = entities
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                return StatusCode(500, "Internal Server Error");
            }
        }

        private IQueryable<Entity> ApplySorting(IQueryable<Entity> query, string sortBy, string sortOrder)
        {
            if (string.IsNullOrEmpty(sortBy))
            {
                return query;
            }

            var entityType = typeof(Entity);
            var propertyName = char.ToUpper(sortBy[0]) + sortBy.Substring(1); // Capitalize the first letter

            var parameter = Expression.Parameter(entityType, "e");
            var property = Expression.Property(parameter, propertyName);
            var lambda = Expression.Lambda(property, parameter);

            var orderByMethodName = sortOrder.ToLower() == "desc" ? "OrderByDescending" : "OrderBy";
            var orderByMethod = typeof(Queryable).GetMethods()
                .Single(method => method.Name == orderByMethodName && method.GetParameters().Length == 2);

            var genericMethod = orderByMethod.MakeGenericMethod(entityType, property.Type);

            var newQuery = (IQueryable<Entity>)genericMethod.Invoke(null, new object[] { query, lambda });

            return newQuery;
        }








        [HttpPut("{id}")]
        [ProducesResponseType(typeof(Entity), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Update(string id, Entity updatedEntity)
        {
            try
            {
                // Retrieve the existing entity with related data
                var entityToUpdate = await _context.Entities
                    .Include(e => e.Addresses)
                    .Include(e => e.Dates)
                    .Include(e => e.Names)
                    .FirstOrDefaultAsync(e => e.Id == id);

                if (entityToUpdate == null)
                {
                    return NotFound("Entity not found");
                }

                // Update properties of the main entity
                entityToUpdate.Deceased = updatedEntity.Deceased;
                entityToUpdate.Gender = updatedEntity.Gender;

                // Update related addresses
                foreach (var updatedAddress in updatedEntity.Addresses)
                {
                    var existingAddress = entityToUpdate.Addresses.FirstOrDefault(a => a.Id == updatedAddress.Id);
                    if (existingAddress != null)
                    {
                        existingAddress.AddressLine = updatedAddress.AddressLine;
                        existingAddress.City = updatedAddress.City;
                        existingAddress.Country = updatedAddress.Country;
                    }
                }

                // Update related dates
                foreach (var updatedDate in updatedEntity.Dates)
                {
                    var existingDate = entityToUpdate.Dates.FirstOrDefault(d => d.Id == updatedDate.Id);
                    if (existingDate != null)
                    {
                        existingDate.DateType = updatedDate.DateType;
                        existingDate.DateValue = updatedDate.DateValue;
                    }
                }

                // Update related names
                foreach (var updatedName in updatedEntity.Names)
                {
                    var existingName = entityToUpdate.Names.FirstOrDefault(n => n.Id == updatedName.Id);
                    if (existingName != null)
                    {
                        existingName.FirstName = updatedName.FirstName;
                        existingName.LastName = updatedName.LastName;
                    }
                }

                // Save changes to the database
                await _context.SaveChangesAsync();

                return Ok(entityToUpdate);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                return StatusCode(500, "Internal Server Error");
            }
        }


        }

    public enum GenderType
    {
        Male,
        Female,
        Other,
        All
    }

    public enum DeceasedType
    {
        IncludeDeceased,
        ExcludeDeceased,
        All
    }

}