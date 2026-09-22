namespace ZynoraHR.Mobile;

public static class AppSettings
{
#if DEBUG
    public const string BaseUrl = "http://127.0.0.1:5102/";
#else
    public const string BaseUrl = "https://zynorahr.com/";
#endif
    public const string EmployeePortalUrl = BaseUrl + "EmployeePortal";
}