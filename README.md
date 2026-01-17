# RAG Demo Application

A full-stack Retrieval-Augmented Generation (RAG) chatbot application with multi-language support (English and Arabic).

## Project Structure

```
RAG-Demo/
├── RAGDemoBackend/    # ASP.NET Core Web API (.NET 8.0)
└── RAGDemoFrontend/   # Angular 21 SPA
```

## Features

- **Multi-language Support**: Full English and Arabic interface with RTL support
- **RAG Implementation**: Vector search powered by Qdrant
- **AI Integration**: GitHub Models API (GPT-4o-mini) support
- **Modern UI**: PrimeNG components with custom styling
- **Document Management**: Upload and search documents
- **Real-time Chat**: Interactive chatbot with formatted responses

## Prerequisites

- .NET 8.0 SDK
- Node.js 18+ and npm
- Qdrant vector database (optional for vector search)
- GitHub Personal Access Token (optional for AI responses)

## Backend Setup

```bash
cd RAGDemoBackend
dotnet restore
dotnet build
dotnet run
```

The backend API will run on `https://localhost:7085`

### Configuration

Update `appsettings.json` or use environment variables:
- `GitHub:Token` - GitHub Personal Access Token for AI models
- `DemoSettings:UseGitHubModels` - Enable/disable AI responses
- Qdrant connection settings

## Frontend Setup

```bash
cd RAGDemoFrontend
npm install
npm start
```

The frontend will run on `http://localhost:4200`

### Environment Configuration

- Development: `src/environments/environment.ts`
- Production: `src/environments/environment.prod.ts`

Update `apiUrl` to match your backend API endpoint.

## Building for Production

### Frontend
```bash
cd RAGDemoFrontend
npm run build
```
Output: `dist/RAGDemoFrontend/`

### Backend
```bash
cd RAGDemoBackend
dotnet publish -c Release
```

## Technology Stack

### Backend
- ASP.NET Core 8.0
- Azure AI Inference SDK
- Qdrant Vector Database
- Local BERT Embeddings

### Frontend
- Angular 21 (Standalone Components)
- PrimeNG 21
- ngx-translate
- TypeScript
- SCSS

## License

MIT
