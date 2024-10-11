// Specify the directory where the .ts and .js files are located

string dir = "./";  // Adjust this path if needed

Console.WriteLine($"Checking path {dir} for .ts files...");

// Get all .ts files in the directory and its subdirectories
string[] tsFiles = Directory.GetFiles(dir, "*.ts", SearchOption.AllDirectories);

Console.WriteLine($"Found {tsFiles.Length} .ts files. Deleting corresponding .js files...");

foreach (var tsFile in tsFiles)
{
    // Replace the .ts extension with .js
    string jsFile = Path.ChangeExtension(tsFile, ".js");

    // Check if the corresponding .js file exists and delete it
    if (File.Exists(jsFile))
    {
        Console.WriteLine($"Deleting {jsFile}");
        File.Delete(jsFile);
    }
    
    // also clean up the .js.map file
    string jsMapFile = Path.ChangeExtension(tsFile, ".js.map");
    if (File.Exists(jsMapFile))
    {
        Console.WriteLine($"Deleting {jsMapFile}");
        File.Delete(jsMapFile);
    }
}

Console.WriteLine("Cleanup complete.");
    