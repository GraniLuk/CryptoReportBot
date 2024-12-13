FROM python:3.9-slim

WORKDIR /app

# Install dependencies and configure locales
RUN apt-get update && apt-get install -y \
    curl \
    locales \
    && sed -i '/en_US.UTF-8/s/^# //g' /etc/locale.gen \
    && locale-gen \
    && curl -sL https://aka.ms/InstallAzureCLIDeb | bash

# Set environment variables for locale
ENV LANG=en_US.UTF-8
ENV LANGUAGE=en_US:en
ENV LC_ALL=en_US.UTF-8

COPY requirements.txt .
RUN pip install -r requirements.txt
COPY . .

CMD ["python", "init.py"]