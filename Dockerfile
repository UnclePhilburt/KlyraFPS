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
COPY serverbuild/ ./

# Make executable
RUN chmod +x ./linuxserver.x86_64

# Expose port
EXPOSE 7777

# Run the server in batch mode
CMD ["./linuxserver.x86_64", "-batchmode", "-nographics", "-logFile", "-"]
