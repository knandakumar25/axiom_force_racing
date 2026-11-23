FROM python:3.15.2-slim-trixie
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg libraqm0 && rm -rf /var/lib/apt/lists/*
RUN pip install --no-cache-dir pillow==13.0.0
WORKDIR /workspace
ENTRYPOINT ["python3"]
