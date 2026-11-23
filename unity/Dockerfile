FROM unity/editor-base:1.0.0
ARG UNITY_VERSION=6000.7.0f1
ARG UNITY_ACCEPT_EDITOR_TERMS
ENV UNITY_NON_INTERACTIVE=1 XDG_DATA_HOME=/opt/unity/data
RUN test "$UNITY_ACCEPT_EDITOR_TERMS" = yes || { echo 'Unity Editor terms acceptance is required'; exit 2; }
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
RUN mkdir -p "$XDG_DATA_HOME" && unity install-path -s /opt/unity/editors \
    && unity install --accept-eula --cm "$UNITY_VERSION" --headless -m linux-il2cpp webgl android windows-coreclr mac-coreclr \
    && unity terms accept "$UNITY_VERSION" --yes \
    && unity cache clean -y \
    && chown -R root:root /opt/unity/editors && chmod -R a+rX /opt/unity/editors \
    && chown -R ubuntu:ubuntu "$XDG_DATA_HOME"
USER ubuntu
WORKDIR /workspace
