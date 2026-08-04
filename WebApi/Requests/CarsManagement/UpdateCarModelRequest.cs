namespace Cable.Requests.CarsManagement;

public record UpdateCarModelRequest( string Name, int CarTypeId, int? SizeId = null );
