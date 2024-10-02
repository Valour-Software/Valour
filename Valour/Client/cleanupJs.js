const fs = require('fs');
const path = require('path');

// Specify the directory where the .ts and .js files are located
const dir = './src'; // Adjust this path to the directory where your TS and JS files are located

fs.readdir(dir, (err, files) => {
    if (err) {
        console.error('Error reading directory:', err);
        return;
    }

    files.forEach(file => {
        // If the file is a TypeScript (.ts) file, look for its corresponding .js file
        if (file.endsWith('.ts')) {
            const jsFile = file.replace('.ts', '.js');
            const jsFilePath = path.join(dir, jsFile);

            // Check if the corresponding .js file exists and delete it
            if (fs.existsSync(jsFilePath)) {
                fs.unlink(jsFilePath, err => {
                    if (err) {
                        console.error(`Error deleting ${jsFile}:`, err);
                    } else {
                        console.log(`Deleted: ${jsFile}`);
                    }
                });
            }
        }
    });
});