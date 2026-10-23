namespace Payments.Migrations
{
    public static class RepoRoot 
    { 
        public static string Find() 
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            
            while (dir != null)
            {
                // Check if Payments.slnx
                if (dir.GetFiles("Payments.slnx").Length > 0) return dir.FullName;
                dir = dir.Parent;
            }
            // If we reach the root of the drive and found nothing, throw an exception
            throw new DirectoryNotFoundException(
                "Could not find repository root. 'Payments.slnx' were not found in any parent directories."
            );
        }
        
    }

}