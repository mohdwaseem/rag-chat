import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CardModule } from 'primeng/card';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { Textarea } from 'primeng/textarea';
import { CheckboxModule } from 'primeng/checkbox';
import { Select } from 'primeng/select';
import { ProgressBarModule } from 'primeng/progressbar';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { DocumentUpload } from '../document-upload/document-upload';
import { ChatService } from '../../services/chat.service';
import { WebContentRequest } from '../../models/chat.model';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

@Component({
  selector: 'app-admin',
  imports: [
    CommonModule,
    FormsModule,
    CardModule,
    ButtonModule,
    InputTextModule,
    Textarea,
    CheckboxModule,
    Select,
    ProgressBarModule,
    ToastModule,
    DocumentUpload,
    TranslateModule
  ],
  providers: [MessageService],
  templateUrl: './admin.html',
  styleUrl: './admin.scss',
  standalone: true
})
export class Admin {
  websiteUrl: string = '';
  includeLinks: boolean = false;
  maxDepth: number = 1;
  isIngesting: boolean = false;
  processedUrls: string[] = [];
  frameworkType: string | null = null;
  customRoutesText: string = '';
  frameworkOptions: any[] = [];

  constructor(
    private chatService: ChatService,
    private messageService: MessageService,
    private translate: TranslateService
  ) {
    // Initialize framework options with translations
    this.frameworkOptions = [
      { label: this.translate.instant('admin.websiteIngestion.frameworks.static'), value: 'static' },
      { label: this.translate.instant('admin.websiteIngestion.frameworks.angular'), value: 'angular' },
      { label: this.translate.instant('admin.websiteIngestion.frameworks.react'), value: 'react' },
      { label: this.translate.instant('admin.websiteIngestion.frameworks.vue'), value: 'vue' }
    ];
  }

  onDocumentUploaded(): void {
    this.messageService.add({
      severity: 'success',
      summary: this.translate.instant('admin.documentUpload.successTitle'),
      detail: this.translate.instant('admin.documentUpload.successMessage'),
      life: 5000
    });
  }

  ingestWebsite(): void {
    if (!this.websiteUrl.trim()) {
      this.messageService.add({
        severity: 'warn',
        summary: this.translate.instant('admin.websiteIngestion.urlRequiredTitle'),
        detail: this.translate.instant('admin.websiteIngestion.urlRequiredMessage'),
        life: 3000
      });
      return;
    }

    // Validate custom routes if framework is selected
    if (this.frameworkType && this.frameworkType !== 'static' && !this.customRoutesText.trim()) {
      this.messageService.add({
        severity: 'warn',
        summary: this.translate.instant('admin.websiteIngestion.routesRequiredTitle'),
        detail: this.translate.instant('admin.websiteIngestion.routesRequiredMessage'),
        life: 3000
      });
      return;
    }

    this.isIngesting = true;
    this.processedUrls = [];

    // Parse custom routes from textarea (comma or newline separated)
    let customRoutes: string[] | undefined = undefined;
    if (this.frameworkType && this.frameworkType !== 'static' && this.customRoutesText.trim()) {
      customRoutes = this.customRoutesText
        .split(/[,\r\n]+/) // Split by comma or newline
        .map(route => route.trim())
        .filter(route => route.length > 0)
        .map(route => route.startsWith('/') ? route : '/' + route); // Ensure routes start with /
    }

    const request: WebContentRequest = {
      url: this.websiteUrl,
      includeLinks: this.includeLinks,
      maxDepth: this.maxDepth,
      frameworkType: this.frameworkType || undefined,
      customRoutes: customRoutes
    };

    this.chatService.ingestUrl(request).subscribe({
      next: (response) => {
        this.isIngesting = false;
        this.processedUrls = response.processedUrls;
        this.messageService.add({
          severity: 'success',
          summary: this.translate.instant('admin.websiteIngestion.successTitle'),
          detail: this.translate.instant('admin.websiteIngestion.successMessage', {
            chunks: response.chunksCreated,
            pages: response.processedUrls.length
          }),
          life: 6000
        });
        this.websiteUrl = '';
        this.customRoutesText = '';
        this.frameworkType = null;
      },
      error: (error) => {
        this.isIngesting = false;
        console.error('Ingestion error:', error);
        this.messageService.add({
          severity: 'error',
          summary: this.translate.instant('admin.websiteIngestion.ingestionFailedTitle'),
          detail: error.error?.message || this.translate.instant('admin.websiteIngestion.ingestionFailedMessage'),
          life: 5000
        });
      }
    });
  }
}
