plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.padlink.app"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.padlink.app"
        minSdk = 26              // Android 8.0+（VibrationEffect 原生支持，覆盖 99%+ 设备）
        targetSdk = 35           // Android 15；Android 16/17 上走兼容模式，无启动限制
        versionCode = 3
        versionName = "1.2"
        resourceConfigurations += listOf("zh", "en")
    }

    buildTypes {
        getByName("release") {
            isMinifyEnabled = false
            // debug 与 release 用同一个密钥，保证覆盖安装不冲突
            signingConfig = signingConfigs.getByName("debug")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
    packaging {
        resources.excludes += setOf(
            "META-INF/*.kotlin_module",
            "META-INF/*.version",
            "META-INF/DEPENDENCIES",
        )
    }
    buildFeatures {
        buildConfig = true
    }
    lint {
        abortOnError = false
    }
}

dependencies {
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.appcompat:appcompat:1.7.0")
    implementation("androidx.activity:activity-ktx:1.9.3")
}
