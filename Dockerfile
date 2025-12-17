# Unity Dedicated Server for Render
FROM ubuntu:22.04

# Install dependencies
RUN apt-get update && apt-get install -y \
    ca-certificates \
    libglu1-mesa \
    && rm -rf /var/lib/apt/lists/*

# Set working directory
WORKDIR /app

# Copy server build files
COPY ServerBuild/ ./

# Make executable
RUN chmod +x ./KlyraFPS

# Expose port (Render will assign PORT env var)
EXPOSE 7777

# Run the server in batch mode
CMD ["./KlyraFPS", "-batchmode", "-nographics", "-logFile", "-"]
